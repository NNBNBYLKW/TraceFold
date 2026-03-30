from __future__ import annotations

from .bot.models import IncomingMessage, OutgoingMessage
from .clients.tracefold_api import TraceFoldApiError


def render_start(message: IncomingMessage) -> OutgoingMessage:
    return _message(
        message,
        "TraceFold Feishu quick capture is ready.\n"
        "Send plain text to create a capture record first.",
    )


def render_help(message: IncomingMessage) -> OutgoingMessage:
    return _message(
        message,
        "Send plain text to record quickly.\n"
        "Use start/help if needed.\n"
        "Capture is stored first. Review or formal follow-up happens later in TraceFold Web.",
    )


def render_unknown_command(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, "Only start/help guidance is available. Send plain text to record quickly.")


def render_unsupported_message(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, "Only simple text messages are supported.")


def render_blank_capture(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, "Text is required.")


def render_capture_success(message: IncomingMessage, capture_result: dict) -> OutgoingMessage:
    route = capture_result.get("route")
    pending_item_id = capture_result.get("pending_item_id")

    if route == "pending" and pending_item_id is not None:
        return _message(message, "Captured first. Pending review created. You can send the next text now.")

    return _message(message, "Captured first. You can send the next text now.")


def render_capture_failure(message: IncomingMessage, error: TraceFoldApiError) -> OutgoingMessage:
    text = _map_api_error(
        error,
        unavailable_text="Not recorded. Service unavailable. Try again later.",
        invalid_codes={"INVALID_CAPTURE_INPUT"},
        invalid_text="Not recorded. Input is invalid.",
        default_text="Not recorded. Try again.",
    )
    return _message(message, text)


def _message(message: IncomingMessage, text: str) -> OutgoingMessage:
    return OutgoingMessage(message_id=message.message_id, chat_id=message.chat_id, text=text)


def _map_api_error(
    error: TraceFoldApiError,
    *,
    unavailable_text: str,
    invalid_codes: set[str] | None = None,
    invalid_text: str = "Input is invalid.",
    default_text: str = "Request failed.",
) -> str:
    if error.status_code is None:
        return unavailable_text

    if invalid_codes and error.error_code in invalid_codes:
        return invalid_text

    return default_text
