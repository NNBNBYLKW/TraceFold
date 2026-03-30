from apps.telegram.app.bot.models import IncomingMessage
from apps.telegram.app.clients.tracefold_api import TraceFoldApiError
from apps.telegram.app.formatting import (
    render_alerts_summary,
    render_capture_failure,
    render_capture_success,
    render_dashboard_summary,
    render_pending_error,
    render_pending_list,
    render_status_summary,
)


def _message() -> IncomingMessage:
    return IncomingMessage(
        chat_id=10,
        user_id=11,
        chat_type="private",
        message_id=12,
        text="test",
    )


def test_capture_success_uses_short_consistent_text():
    result = render_capture_success(
        _message(),
        {
            "route": "formal",
            "status": "committed",
            "target_domain": "expense",
            "pending_item_id": None,
        },
    )

    assert result.text == "Captured first. You can send the next text now."


def test_pending_list_empty_state_is_short_and_consistent():
    result = render_pending_list(_message(), {"items": []}, limit=5)

    assert result.text == "No open pending items."


def test_dashboard_summary_renders_short_counts():
    result = render_dashboard_summary(
        _message(),
        {
            "pending_summary": {"open_count": 2},
            "expense_summary": {"count": 3},
            "knowledge_summary": {"count": 4},
            "health_summary": {"count": 5},
        },
    )

    assert "Dashboard summary:" in result.text
    assert "Pending: 2" in result.text


def test_alerts_summary_empty_state_is_short_and_consistent():
    result = render_alerts_summary(_message(), {"items": []}, limit=5)

    assert result.text == "No open alerts."


def test_status_summary_is_short():
    result = render_status_summary(_message(), {"status": "ok"})

    assert result.text == "Service status: ok."


def test_error_mapping_hides_internal_error_text():
    result = render_capture_failure(
        _message(),
        TraceFoldApiError(
            "Very detailed internal failure message.",
            status_code=500,
            error_code="INTERNAL_SERVER_ERROR",
        ),
    )

    assert result.text == "Not recorded. Try again."


def test_pending_error_mapping_uses_stable_not_found_text():
    result = render_pending_error(
        _message(),
        TraceFoldApiError(
            "Pending item missing.",
            status_code=404,
            error_code="PENDING_ITEM_NOT_FOUND",
        ),
    )

    assert result.text == "Pending item not found."
