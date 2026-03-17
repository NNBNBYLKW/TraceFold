from apps.telegram.app.bot.handlers import TelegramMessageHandler
from apps.telegram.app.clients.tracefold_api import TraceFoldApiError


class FakeTraceFoldSummaryApiClient:
    def __init__(self):
        self.calls = []
        self.dashboard_result = {
            "pending_summary": {"open_count": 3},
            "expense_summary": {"count": 12},
            "knowledge_summary": {"count": 4},
            "health_summary": {"count": 2},
            "recent_activity": [{"id": 1}, {"id": 2}],
        }
        self.alerts_result = {
            "items": [
                {
                    "id": 9,
                    "priority": "high",
                    "title": "High heart rate alert",
                    "status": "open",
                }
            ]
        }
        self.status_result = {"status": "ok"}
        self.error = None
        self.capture_calls = []

    def get_dashboard(self):
        self.calls.append(("get_dashboard",))
        if self.error:
            raise self.error
        return self.dashboard_result

    def get_alerts(self):
        self.calls.append(("get_alerts",))
        if self.error:
            raise self.error
        return self.alerts_result

    def get_status(self):
        self.calls.append(("get_status",))
        if self.error:
            raise self.error
        return self.status_result

    def submit_capture(self, *, raw_text, source_type="telegram", source_ref=None):
        self.capture_calls.append(raw_text)
        return {}


def _make_update(text: str) -> dict:
    return {
        "message": {
            "message_id": 9,
            "chat": {"id": 10, "type": "private"},
            "from": {"id": 11},
            "text": text,
        }
    }


def test_dashboard_command_returns_short_summary():
    api_client = FakeTraceFoldSummaryApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/dashboard"))

    assert result is not None
    assert "Dashboard:" in result.text
    assert "pending: 3" in result.text
    assert ("get_dashboard",) in api_client.calls
    assert api_client.capture_calls == []


def test_alerts_command_returns_short_summary():
    api_client = FakeTraceFoldSummaryApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/alerts"))

    assert result is not None
    assert "Alerts:" in result.text
    assert "#9 [high]" in result.text
    assert ("get_alerts",) in api_client.calls


def test_alerts_command_returns_empty_state():
    api_client = FakeTraceFoldSummaryApiClient()
    api_client.alerts_result = {"items": []}
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/alerts"))

    assert result is not None
    assert result.text == "No open alerts."


def test_status_command_returns_minimal_status():
    api_client = FakeTraceFoldSummaryApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/status"))

    assert result is not None
    assert result.text == "Status: ok."
    assert ("get_status",) in api_client.calls


def test_dashboard_command_maps_api_unavailable():
    api_client = FakeTraceFoldSummaryApiClient()
    api_client.error = TraceFoldApiError("TraceFold API is unavailable.")
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/dashboard"))

    assert result is not None
    assert result.text == "Dashboard unavailable."


def test_status_command_maps_api_failure():
    api_client = FakeTraceFoldSummaryApiClient()
    api_client.error = TraceFoldApiError(
        "Internal server error.",
        status_code=500,
        error_code="INTERNAL_SERVER_ERROR",
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/status"))

    assert result is not None
    assert result.text == "Status check failed."


def test_summary_commands_do_not_fall_back_to_capture_submission():
    api_client = FakeTraceFoldSummaryApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    handler.handle_update(_make_update("/dashboard"))
    handler.handle_update(_make_update("/alerts"))
    handler.handle_update(_make_update("/status"))

    assert api_client.capture_calls == []
