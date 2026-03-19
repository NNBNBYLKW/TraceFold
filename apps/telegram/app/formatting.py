from __future__ import annotations

from .bot.models import IncomingMessage, OutgoingMessage
from .clients.tracefold_api import TraceFoldApiError

_SERVICE_UNAVAILABLE_TEXT = "Service status unavailable. Try again later."
_INVALID_INPUT_TEXT = "Input is invalid."
_EMPTY_TEXT_REQUIRED = "Text is required."
_NO_OPEN_PENDING_TEXT = "No open pending items."
_NO_OPEN_ALERTS_TEXT = "No open alerts."


def render_start(message: IncomingMessage) -> OutgoingMessage:
    return _message(
        message,
        "TraceFold Telegram adapter is online.\n"
        "Step 7 Chapter 2 currently exposes only lightweight entry commands.",
    )


def render_help(message: IncomingMessage) -> OutgoingMessage:
    return _message(
        message,
        "Available commands:\n"
        "/start\n"
        "/help\n"
        "/capture <text>\n"
        "/pending\n"
        "/pending <id>\n"
        "/confirm <id>\n"
        "/discard <id>\n"
        "/fix <id> <text>\n"
        "/dashboard\n"
        "/alerts\n"
        "/status",
    )


def render_unknown_command(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, "This command is not available.")


def render_unsupported_message(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, "Only simple private text messages are supported.")


def render_blank_capture(message: IncomingMessage) -> OutgoingMessage:
    return _message(message, _EMPTY_TEXT_REQUIRED)


def render_capture_success(
    message: IncomingMessage,
    capture_result: dict,
) -> OutgoingMessage:
    status = capture_result.get("status")
    pending_item_id = capture_result.get("pending_item_id")
    target_domain = capture_result.get("target_domain")

    if status == "pending" and pending_item_id is not None:
        return _message(message, f"Recorded. Pending item: #{pending_item_id}")

    if status == "committed" and target_domain:
        return _message(message, f"Recorded. Added to {target_domain} record.")

    return _message(message, "Recorded.")


def render_capture_failure(
    message: IncomingMessage,
    error: TraceFoldApiError,
) -> OutgoingMessage:
    text = _map_api_error(
        error,
        invalid_codes={"INVALID_CAPTURE_INPUT"},
        invalid_text=_INVALID_INPUT_TEXT,
        default_text="Capture failed.",
    )
    return _message(message, text)


def render_missing_pending_argument(
    message: IncomingMessage,
    usage: str,
) -> OutgoingMessage:
    return _message(message, usage)


def render_pending_list(
    message: IncomingMessage,
    payload: dict | list,
    *,
    limit: int,
) -> OutgoingMessage:
    items = _extract_pending_items(payload)
    if not items:
        return _message(message, _NO_OPEN_PENDING_TEXT)

    lines = ["Open pending items:"]
    for item in items[:limit]:
        pending_id = item.get("id") or item.get("pending_item_id") or "?"
        target_domain = item.get("target_domain") or "unknown"
        status = item.get("status") or "pending"
        summary = _summarize_pending_payload(item)
        lines.append(f"#{pending_id} [{target_domain}] {status} - {summary}")

    return _message(message, "\n".join(lines))


def render_pending_detail(message: IncomingMessage, item: dict) -> OutgoingMessage:
    pending_id = item.get("id") or item.get("pending_item_id") or "?"
    target_domain = item.get("target_domain") or "unknown"
    status = item.get("status") or "pending"
    proposed = _shorten_text(_summarize_dict(item.get("proposed_payload_json")))
    corrected = _shorten_text(_summarize_dict(item.get("corrected_payload_json")))

    lines = [
        f"Pending item #{pending_id}",
        f"Status: {status}",
        f"Target domain: {target_domain}",
    ]
    if proposed:
        lines.append(f"Proposed: {proposed}")
    if corrected:
        lines.append(f"Corrected: {corrected}")

    return _message(message, "\n".join(lines))


def render_pending_action_success(
    message: IncomingMessage,
    *,
    action: str,
    result: dict,
) -> OutgoingMessage:
    pending_id = result.get("pending_item_id") or result.get("pending_id") or "?"
    status = result.get("status") or "resolved"

    action_label = {
        "confirm": "Confirmed",
        "discard": "Discarded",
        "fix": "Updated",
    }.get(action, "Updated")
    return _message(message, f"{action_label} pending item #{pending_id}. Status: {status}.")


def render_pending_error(
    message: IncomingMessage,
    error: TraceFoldApiError,
) -> OutgoingMessage:
    text = _map_api_error(
        error,
        not_found_codes={"PENDING_ITEM_NOT_FOUND"},
        not_found_text="Pending item not found.",
        resolved_codes={
            "PENDING_ITEM_ALREADY_RESOLVED",
            "PENDING_ITEM_INVALID_STATE",
            "PENDING_ITEM_ALREADY_PROCESSED",
        },
        resolved_text="Pending item already resolved.",
        invalid_codes={"INVALID_PENDING_FIX_INPUT", "INVALID_FIX_INPUT"},
        invalid_text="Fix text is invalid.",
        default_text="Pending action failed.",
    )
    return _message(message, text)


def render_dashboard_summary(message: IncomingMessage, payload: dict) -> OutgoingMessage:
    pending_count = _extract_count(payload, "pending_summary", "open_count")
    expense_count = _extract_count(payload, "expense_summary", "count")
    knowledge_count = _extract_count(payload, "knowledge_summary", "count")
    health_count = _extract_count(payload, "health_summary", "count")

    lines = [
        "Dashboard summary:",
        f"Pending: {pending_count}",
        f"Expense records: {expense_count}",
        f"Knowledge records: {knowledge_count}",
        f"Health records: {health_count}",
    ]

    recent_activity = payload.get("recent_activity")
    recent_count = len(recent_activity) if isinstance(recent_activity, list) else 0
    if recent_count:
        lines.append(f"Recent activity: {recent_count}")

    return _message(message, "\n".join(lines))


def render_alerts_summary(
    message: IncomingMessage,
    payload: dict | list,
    *,
    limit: int = 5,
) -> OutgoingMessage:
    items = _extract_alert_items(payload)
    if not items:
        return _message(message, _NO_OPEN_ALERTS_TEXT)

    lines = ["Open rule alerts:"]
    for item in items[:limit]:
        alert_id = item.get("id") or item.get("alert_id") or "?"
        priority = item.get("priority") or item.get("severity") or "unknown"
        title = item.get("title") or item.get("message") or item.get("kind") or "alert"
        status = item.get("status")
        if status and status != "open":
            continue
        lines.append(f"#{alert_id} [{priority}] {_shorten_text(str(title), 40)}")

    if len(lines) == 1:
        return _message(message, _NO_OPEN_ALERTS_TEXT)

    return _message(message, "\n".join(lines))


def render_status_summary(message: IncomingMessage, payload: dict) -> OutgoingMessage:
    status = payload.get("status") or "unknown"
    detail = payload.get("message") or payload.get("detail")
    if detail:
        return _message(message, f"Service status: {status}. {_shorten_text(str(detail), 40)}")
    return _message(message, f"Service status: {status}.")


def render_status_error(
    message: IncomingMessage,
    error: TraceFoldApiError,
) -> OutgoingMessage:
    text = _map_api_error(
        error,
        unavailable_text="Service status unavailable.",
        default_text="Status check failed.",
    )
    return _message(message, text)


def render_summary_error(
    message: IncomingMessage,
    error: TraceFoldApiError,
    *,
    subject: str,
) -> OutgoingMessage:
    text = _map_api_error(
        error,
        unavailable_text=f"{subject} unavailable.",
        default_text=f"{subject} unavailable right now.",
    )
    return _message(message, text)


def _message(message: IncomingMessage, text: str) -> OutgoingMessage:
    return OutgoingMessage(chat_id=message.chat_id, text=text)


def _map_api_error(
    error: TraceFoldApiError,
    *,
    unavailable_text: str = _SERVICE_UNAVAILABLE_TEXT,
    invalid_codes: set[str] | None = None,
    invalid_text: str = _INVALID_INPUT_TEXT,
    not_found_codes: set[str] | None = None,
    not_found_text: str = "Not found.",
    resolved_codes: set[str] | None = None,
    resolved_text: str = "Already resolved.",
    default_text: str = "Request failed.",
) -> str:
    if error.status_code is None:
        return unavailable_text

    if invalid_codes and error.error_code in invalid_codes:
        return invalid_text

    if not_found_codes and error.error_code in not_found_codes:
        return not_found_text

    if resolved_codes and error.error_code in resolved_codes:
        return resolved_text

    return default_text


def _extract_pending_items(payload: dict | list) -> list[dict]:
    if isinstance(payload, list):
        return [item for item in payload if isinstance(item, dict)]

    if not isinstance(payload, dict):
        return []

    items = payload.get("items")
    if isinstance(items, list):
        return [item for item in items if isinstance(item, dict)]

    data = payload.get("data")
    if isinstance(data, list):
        return [item for item in data if isinstance(item, dict)]

    return []


def _extract_alert_items(payload: dict | list) -> list[dict]:
    if isinstance(payload, list):
        return [item for item in payload if isinstance(item, dict)]

    if not isinstance(payload, dict):
        return []

    for key in ("items", "alerts", "open_alerts", "data"):
        items = payload.get(key)
        if isinstance(items, list):
            return [item for item in items if isinstance(item, dict)]

    return []


def _extract_count(payload: dict, section_key: str, field_key: str) -> int:
    section = payload.get(section_key)
    if isinstance(section, dict):
        value = section.get(field_key)
        if isinstance(value, int):
            return value
    return 0


def _summarize_pending_payload(item: dict) -> str:
    corrected = _summarize_dict(item.get("corrected_payload_json"))
    proposed = _summarize_dict(item.get("proposed_payload_json"))
    text = corrected or proposed
    return _shorten_text(text or "No summary")


def _summarize_dict(value: object) -> str:
    if not isinstance(value, dict):
        return ""

    preferred_keys = ("raw_text", "text", "title", "note", "amount", "summary")
    for key in preferred_keys:
        if key in value and value[key] is not None:
            return str(value[key])

    first_items = []
    for key, item in value.items():
        if item is None:
            continue
        first_items.append(f"{key}={item}")
        if len(first_items) == 2:
            break
    return ", ".join(first_items)


def _shorten_text(text: str, limit: int = 48) -> str:
    if len(text) <= limit:
        return text
    return text[: limit - 3] + "..."
