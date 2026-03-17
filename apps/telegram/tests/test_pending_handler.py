from apps.telegram.app.bot.handlers import TelegramMessageHandler
from apps.telegram.app.clients.tracefold_api import TraceFoldApiError


class FakeTraceFoldPendingApiClient:
    def __init__(self):
        self.calls = []
        self.pending_list_result = {
            "items": [
                {
                    "id": 12,
                    "status": "pending",
                    "target_domain": "expense",
                    "proposed_payload_json": {"raw_text": "Lunch 25"},
                }
            ]
        }
        self.pending_detail_result = {
            "id": 12,
            "status": "pending",
            "target_domain": "expense",
            "proposed_payload_json": {"raw_text": "Lunch 25"},
            "corrected_payload_json": {"raw_text": "Lunch 28"},
        }
        self.confirm_result = {"pending_item_id": 12, "status": "confirmed"}
        self.discard_result = {"pending_item_id": 12, "status": "discarded"}
        self.fix_result = {"pending_item_id": 12, "status": "pending"}
        self.error = None
        self.capture_calls = []

    def get_pending_list(self, *, limit=5, offset=0, status="pending"):
        self.calls.append(("get_pending_list", limit, offset, status))
        if self.error:
            raise self.error
        return self.pending_list_result

    def get_pending_detail(self, pending_id):
        self.calls.append(("get_pending_detail", pending_id))
        if self.error:
            raise self.error
        return self.pending_detail_result

    def confirm_pending(self, pending_id):
        self.calls.append(("confirm_pending", pending_id))
        if self.error:
            raise self.error
        return self.confirm_result

    def discard_pending(self, pending_id):
        self.calls.append(("discard_pending", pending_id))
        if self.error:
            raise self.error
        return self.discard_result

    def fix_pending(self, pending_id, correction_text):
        self.calls.append(("fix_pending", pending_id, correction_text))
        if self.error:
            raise self.error
        return self.fix_result

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


def test_pending_list_command_returns_short_summary():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/pending"))

    assert result is not None
    assert "Open pending:" in result.text
    assert "#12" in result.text
    assert ("get_pending_list", 5, 0, "pending") in api_client.calls
    assert api_client.capture_calls == []


def test_pending_detail_command_returns_action_context():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/pending 12"))

    assert result is not None
    assert "Pending #12" in result.text
    assert "domain: expense" in result.text
    assert ("get_pending_detail", 12) in api_client.calls


def test_confirm_command_returns_short_success():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/confirm 12"))

    assert result is not None
    assert result.text == "Confirmed pending #12. Status: confirmed."
    assert ("confirm_pending", 12) in api_client.calls


def test_confirm_command_maps_not_found_error():
    api_client = FakeTraceFoldPendingApiClient()
    api_client.error = TraceFoldApiError(
        "Pending item not found.",
        status_code=404,
        error_code="PENDING_ITEM_NOT_FOUND",
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/confirm 999"))

    assert result is not None
    assert result.text == "Pending item not found."


def test_confirm_command_maps_already_resolved_error():
    api_client = FakeTraceFoldPendingApiClient()
    api_client.error = TraceFoldApiError(
        "Pending item already resolved.",
        status_code=409,
        error_code="PENDING_ITEM_ALREADY_RESOLVED",
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/confirm 12"))

    assert result is not None
    assert result.text == "Pending item already resolved."


def test_discard_command_returns_short_success():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/discard 12"))

    assert result is not None
    assert result.text == "Discarded pending #12. Status: discarded."
    assert ("discard_pending", 12) in api_client.calls


def test_fix_command_returns_short_success():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/fix 12 Lunch 28"))

    assert result is not None
    assert result.text == "Updated pending #12. Status: pending."
    assert ("fix_pending", 12, "Lunch 28") in api_client.calls


def test_fix_command_requires_correction_text():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/fix 12"))

    assert result is not None
    assert result.text == "Fix text is required."
    assert api_client.calls == []


def test_fix_command_maps_invalid_fix_input_error():
    api_client = FakeTraceFoldPendingApiClient()
    api_client.error = TraceFoldApiError(
        "Fix input is invalid.",
        status_code=400,
        error_code="INVALID_PENDING_FIX_INPUT",
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/fix 12 corrected"))

    assert result is not None
    assert result.text == "Fix text is invalid."


def test_pending_command_maps_service_unavailable():
    api_client = FakeTraceFoldPendingApiClient()
    api_client.error = TraceFoldApiError("TraceFold API is unavailable.")
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_make_update("/pending"))

    assert result is not None
    assert result.text == "Service unavailable. Try again later."


def test_pending_commands_do_not_fall_back_to_capture_submission():
    api_client = FakeTraceFoldPendingApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    handler.handle_update(_make_update("/pending"))
    handler.handle_update(_make_update("/confirm 12"))
    handler.handle_update(_make_update("/discard 12"))
    handler.handle_update(_make_update("/fix 12 corrected"))

    assert api_client.capture_calls == []
