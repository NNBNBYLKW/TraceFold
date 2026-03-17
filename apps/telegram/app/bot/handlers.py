from __future__ import annotations

from .dispatch import TelegramCommandDispatcher
from .models import CommandContext, IncomingMessage, OutgoingMessage
from ..clients.tracefold_api import TraceFoldApiClient, TraceFoldApiError
from ..formatting import (
    render_alerts_summary,
    render_blank_capture,
    render_capture_failure,
    render_capture_success,
    render_dashboard_summary,
    render_missing_pending_argument,
    render_pending_action_success,
    render_pending_detail,
    render_pending_error,
    render_pending_list,
    render_status_error,
    render_status_summary,
    render_summary_error,
    render_unsupported_message,
)
from ..observability import log_adapter_event


class TelegramMessageHandler:
    _PENDING_LIST_LIMIT = 5
    _ALERT_LIST_LIMIT = 5

    def __init__(
        self,
        *,
        tracefold_api: TraceFoldApiClient | None = None,
        dispatcher: TelegramCommandDispatcher | None = None,
    ) -> None:
        self._tracefold_api = tracefold_api
        self._dispatcher = dispatcher or TelegramCommandDispatcher()

    def handle_update(self, update: dict) -> OutgoingMessage | None:
        message = update.get("message") or {}
        chat = message.get("chat") or {}
        from_user = message.get("from") or {}

        chat_id = chat.get("id")
        if chat_id is None:
            return None

        incoming = IncomingMessage(
            chat_id=chat_id,
            user_id=from_user.get("id"),
            chat_type=chat.get("type"),
            message_id=message.get("message_id"),
            text=message.get("text"),
        )

        if incoming.text is None:
            self._log_local(incoming, category="non_text", outcome="ignored")
            return render_unsupported_message(incoming)

        text = incoming.text.strip()
        if incoming.chat_type == "private" and not text:
            self._log_local(incoming, category="blank_text", outcome="rejected")
            return render_blank_capture(incoming)

        if not text.startswith("/"):
            if incoming.chat_type != "private":
                self._log_local(incoming, category="non_private_text", outcome="ignored")
                return render_unsupported_message(incoming)
            return self._submit_capture(incoming, text)

        command, _, arguments = text.partition(" ")
        command = command.lower()
        arguments = arguments.strip()

        if command == "/capture":
            if not arguments:
                self._log_local(incoming, command=command, category="capture", outcome="rejected")
                return render_blank_capture(incoming)
            return self._submit_capture(incoming, arguments, command=command)

        if command == "/pending":
            return self._handle_pending_command(incoming, arguments, command=command)

        if command == "/dashboard":
            return self._handle_dashboard_command(incoming, command=command)

        if command == "/alerts":
            return self._handle_alerts_command(incoming, command=command)

        if command == "/status":
            return self._handle_status_command(incoming, command=command)

        if command == "/confirm":
            return self._handle_pending_action_command(
                incoming,
                action="confirm",
                arguments=arguments,
                command=command,
            )

        if command == "/discard":
            return self._handle_pending_action_command(
                incoming,
                action="discard",
                arguments=arguments,
                command=command,
            )

        if command == "/fix":
            return self._handle_fix_command(incoming, arguments, command=command)

        context = CommandContext(
            message=incoming,
            command=command,
            arguments=arguments,
        )
        self._log_local(incoming, command=command, category="command", outcome="handled")
        return self._dispatcher.dispatch(context)

    def _submit_capture(
        self,
        message: IncomingMessage,
        raw_text: str,
        *,
        command: str | None = None,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="capture", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            result = self._tracefold_api.submit_capture(
                raw_text=raw_text,
                source_type="telegram",
                source_ref=self._build_source_ref(message),
            )
        except TraceFoldApiError as exc:
            self._log_api(
                message,
                command=command or "plain_text",
                endpoint="capture_submit",
                outcome="failure",
                error=exc,
            )
            return render_capture_failure(message, exc)

        self._log_api(
            message,
            command=command or "plain_text",
            endpoint="capture_submit",
            outcome="success",
        )
        return render_capture_success(message, result)

    def _handle_pending_command(
        self,
        message: IncomingMessage,
        arguments: str,
        *,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="pending", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            if not arguments:
                result = self._tracefold_api.get_pending_list(limit=self._PENDING_LIST_LIMIT)
                self._log_api(message, command=command, endpoint="pending_list", outcome="success")
                return render_pending_list(message, result, limit=self._PENDING_LIST_LIMIT)

            pending_id = int(arguments)
            detail = self._tracefold_api.get_pending_detail(pending_id)
            self._log_api(message, command=command, endpoint="pending_detail", outcome="success")
            return render_pending_detail(message, detail)
        except ValueError:
            self._log_local(message, command=command, category="pending", outcome="rejected")
            return render_missing_pending_argument(message, "Usage: /pending or /pending <id>")
        except TraceFoldApiError as exc:
            endpoint = "pending_detail" if arguments else "pending_list"
            self._log_api(message, command=command, endpoint=endpoint, outcome="failure", error=exc)
            return render_pending_error(message, exc)

    def _handle_pending_action_command(
        self,
        message: IncomingMessage,
        *,
        action: str,
        arguments: str,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category=action, outcome="unsupported")
            return render_unsupported_message(message)

        try:
            pending_id = int(arguments)
        except ValueError:
            self._log_local(message, command=command, category=action, outcome="rejected")
            return render_missing_pending_argument(message, f"Usage: /{action} <id>")

        try:
            if action == "confirm":
                result = self._tracefold_api.confirm_pending(pending_id)
                endpoint = "pending_confirm"
            else:
                result = self._tracefold_api.discard_pending(pending_id)
                endpoint = "pending_discard"
        except TraceFoldApiError as exc:
            self._log_api(message, command=command, endpoint=f"pending_{action}", outcome="failure", error=exc)
            return render_pending_error(message, exc)

        self._log_api(message, command=command, endpoint=endpoint, outcome="success")
        return render_pending_action_success(message, action=action, result=result)

    def _handle_fix_command(
        self,
        message: IncomingMessage,
        arguments: str,
        *,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="fix", outcome="unsupported")
            return render_unsupported_message(message)

        pending_id_text, _, correction_text = arguments.partition(" ")
        if not pending_id_text:
            self._log_local(message, command=command, category="fix", outcome="rejected")
            return render_missing_pending_argument(message, "Usage: /fix <id> <text>")

        try:
            pending_id = int(pending_id_text)
        except ValueError:
            self._log_local(message, command=command, category="fix", outcome="rejected")
            return render_missing_pending_argument(message, "Usage: /fix <id> <text>")

        if not correction_text.strip():
            self._log_local(message, command=command, category="fix", outcome="rejected")
            return render_missing_pending_argument(message, "Fix text is required.")

        try:
            result = self._tracefold_api.fix_pending(pending_id, correction_text.strip())
        except TraceFoldApiError as exc:
            self._log_api(message, command=command, endpoint="pending_fix", outcome="failure", error=exc)
            return render_pending_error(message, exc)

        self._log_api(message, command=command, endpoint="pending_fix", outcome="success")
        return render_pending_action_success(message, action="fix", result=result)

    def _handle_dashboard_command(
        self,
        message: IncomingMessage,
        *,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="dashboard", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            payload = self._tracefold_api.get_dashboard()
        except TraceFoldApiError as exc:
            self._log_api(message, command=command, endpoint="dashboard", outcome="failure", error=exc)
            return render_summary_error(message, exc, subject="Dashboard")

        self._log_api(message, command=command, endpoint="dashboard", outcome="success")
        return render_dashboard_summary(message, payload)

    def _handle_alerts_command(
        self,
        message: IncomingMessage,
        *,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="alerts", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            payload = self._tracefold_api.get_alerts()
        except TraceFoldApiError as exc:
            self._log_api(message, command=command, endpoint="alerts", outcome="failure", error=exc)
            return render_summary_error(message, exc, subject="Alerts")

        self._log_api(message, command=command, endpoint="alerts", outcome="success")
        return render_alerts_summary(message, payload, limit=self._ALERT_LIST_LIMIT)

    def _handle_status_command(
        self,
        message: IncomingMessage,
        *,
        command: str,
    ) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, command=command, category="status", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            payload = self._tracefold_api.get_status()
        except TraceFoldApiError as exc:
            self._log_api(message, command=command, endpoint="status", outcome="failure", error=exc)
            return render_status_error(message, exc)

        self._log_api(message, command=command, endpoint="status", outcome="success")
        return render_status_summary(message, payload)

    @staticmethod
    def _build_source_ref(message: IncomingMessage) -> str:
        parts = [f"chat:{message.chat_id}"]
        if message.message_id is not None:
            parts.append(f"message:{message.message_id}")
        if message.user_id is not None:
            parts.append(f"user:{message.user_id}")
        return ":".join(parts)

    @staticmethod
    def _log_local(
        message: IncomingMessage,
        *,
        category: str,
        outcome: str,
        command: str | None = None,
    ) -> None:
        log_adapter_event(
            command=command,
            message_type=category,
            chat_id=message.chat_id,
            message_id=message.message_id,
            endpoint="local",
            outcome=outcome,
        )

    @staticmethod
    def _log_api(
        message: IncomingMessage,
        *,
        command: str,
        endpoint: str,
        outcome: str,
        error: TraceFoldApiError | None = None,
    ) -> None:
        log_adapter_event(
            command=command,
            message_type="command",
            chat_id=message.chat_id,
            message_id=message.message_id,
            endpoint=endpoint,
            outcome=outcome,
            error_code=error.error_code if error else None,
        )
