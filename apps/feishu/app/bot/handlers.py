from __future__ import annotations

import json

from .models import IncomingMessage, OutgoingMessage
from ..clients.tracefold_api import TraceFoldApiClient, TraceFoldApiError
from ..formatting import (
    render_blank_capture,
    render_capture_failure,
    render_capture_success,
    render_help,
    render_start,
    render_unknown_command,
    render_unsupported_message,
)
from ..observability import log_adapter_event

_START_TEXTS = {"/start", "start"}
_HELP_TEXTS = {"/help", "help"}


class FeishuMessageHandler:
    def __init__(self, *, tracefold_api: TraceFoldApiClient | None = None) -> None:
        self._tracefold_api = tracefold_api

    def handle_event(self, payload: dict) -> OutgoingMessage | None:
        header = payload.get("header") or {}
        if header.get("event_type") != "im.message.receive_v1":
            return None

        event = payload.get("event") or {}
        message = event.get("message") or {}
        sender = event.get("sender") or {}

        message_id = message.get("message_id")
        chat_id = message.get("chat_id")
        if not isinstance(message_id, str) or not message_id or not isinstance(chat_id, str) or not chat_id:
            return None

        incoming = IncomingMessage(
            chat_id=chat_id,
            user_id=self._extract_sender_id(sender),
            chat_type=message.get("chat_type"),
            message_id=message_id,
            text=self._extract_text(message),
        )

        if message.get("message_type") != "text" or incoming.text is None:
            self._log_local(incoming, category="non_text", outcome="ignored")
            return render_unsupported_message(incoming)

        text = incoming.text.strip()
        if not text:
            self._log_local(incoming, category="blank_text", outcome="rejected")
            return render_blank_capture(incoming)

        lowered = text.lower()
        if lowered in _START_TEXTS:
            self._log_local(incoming, command="start", category="command", outcome="handled")
            return render_start(incoming)
        if lowered in _HELP_TEXTS:
            self._log_local(incoming, command="help", category="command", outcome="handled")
            return render_help(incoming)
        if text.startswith("/"):
            self._log_local(incoming, command=lowered, category="command", outcome="unsupported")
            return render_unknown_command(incoming)

        return self._submit_capture(incoming, text)

    def _submit_capture(self, message: IncomingMessage, raw_text: str) -> OutgoingMessage:
        if self._tracefold_api is None:
            self._log_local(message, category="capture", outcome="unsupported")
            return render_unsupported_message(message)

        try:
            result = self._tracefold_api.submit_capture(
                raw_text=raw_text,
                source_type="feishu",
                source_ref=self._build_source_ref(message),
            )
        except TraceFoldApiError as exc:
            self._log_api(
                message,
                command="plain_text",
                endpoint="capture_submit",
                outcome="failure",
                error=exc,
            )
            return render_capture_failure(message, exc)

        self._log_api(
            message,
            command="plain_text",
            endpoint="capture_submit",
            outcome="success",
        )
        return render_capture_success(message, result)

    @staticmethod
    def _extract_text(message: dict) -> str | None:
        content = message.get("content")
        if isinstance(content, dict):
            text = content.get("text")
            return text if isinstance(text, str) else None
        if not isinstance(content, str):
            return None

        try:
            parsed = json.loads(content)
        except ValueError:
            return None
        text = parsed.get("text")
        return text if isinstance(text, str) else None

    @staticmethod
    def _extract_sender_id(sender: dict) -> str | None:
        sender_id = sender.get("sender_id")
        if isinstance(sender_id, dict):
            for key in ("open_id", "user_id", "union_id"):
                value = sender_id.get(key)
                if isinstance(value, str) and value:
                    return value
        return None

    @staticmethod
    def _build_source_ref(message: IncomingMessage) -> str:
        parts = [f"chat:{message.chat_id}", f"message:{message.message_id}"]
        if message.user_id:
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
