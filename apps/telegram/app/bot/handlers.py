from __future__ import annotations

from .dispatch import TelegramCommandDispatcher
from .models import CommandContext, IncomingMessage, OutgoingMessage
from ..clients.tracefold_api import TraceFoldApiClient, TraceFoldApiError
from ..formatting import (
    render_blank_capture,
    render_capture_failure,
    render_capture_success,
    render_unsupported_message,
)
from ..observability import log_adapter_event


class TelegramMessageHandler:
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

        context = CommandContext(
            message=incoming,
            command=command,
            arguments=arguments,
        )
        supported = command in {"/start", "/help"}
        self._log_local(
            incoming,
            command=command,
            category="command",
            outcome="handled" if supported else "unsupported",
        )
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
