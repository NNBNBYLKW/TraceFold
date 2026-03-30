from apps.telegram.app.bot.handlers import TelegramMessageHandler
from apps.telegram.app.clients.tracefold_api import TraceFoldApiError


class FakeTraceFoldApiClient:
    def __init__(self, result=None, error=None):
        self.result = result
        self.error = error
        self.calls = []

    def submit_capture(self, *, raw_text, source_type="telegram", source_ref=None):
        self.calls.append(
            {
                "raw_text": raw_text,
                "source_type": source_type,
                "source_ref": source_ref,
            }
        )
        if self.error is not None:
            raise self.error
        return self.result


def test_private_plain_text_submits_capture_and_returns_pending_feedback():
    api_client = FakeTraceFoldApiClient(
        result={
            "capture_created": True,
            "capture_id": 21,
            "status": "pending",
            "route": "pending",
            "target_domain": "expense",
            "pending_item_id": 88,
            "formal_record_id": None,
        }
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 9,
                "chat": {"id": 10, "type": "private"},
                "from": {"id": 11},
                "text": "今天午饭 25 元",
            }
        }
    )

    assert result is not None
    assert result.text == "Captured first. Pending review created. You can send the next text now."
    assert api_client.calls == [
        {
            "raw_text": "今天午饭 25 元",
            "source_type": "telegram",
            "source_ref": "chat:10:message:9:user:11",
        }
    ]


def test_start_and_help_commands_remain_minimal():
    handler = TelegramMessageHandler()

    start_result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "/start",
            }
        }
    )
    help_result = handler.handle_update(
        {
            "message": {
                "message_id": 11,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "/help",
            }
        }
    )

    assert start_result is not None
    assert help_result is not None
    assert "quick capture is ready" in start_result.text.lower()
    assert "capture record first" in start_result.text.lower()
    assert "send plain text" in help_result.text.lower()
    assert "/start" in help_result.text
    assert "/help" in help_result.text


def test_formal_route_still_returns_capture_first_feedback():
    api_client = FakeTraceFoldApiClient(
        result={
            "capture_created": True,
            "capture_id": 22,
            "status": "committed",
            "route": "formal",
            "target_domain": "expense",
            "pending_item_id": None,
            "formal_record_id": 7,
        }
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "今天午饭 25 元",
            }
        }
    )

    assert result is not None
    assert result.text == "Captured first. You can send the next text now."


def test_capture_failure_returns_short_unavailable_feedback():
    api_client = FakeTraceFoldApiClient(
        error=TraceFoldApiError("TraceFold API is unavailable.")
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "记一条测试",
            }
        }
    )

    assert result is not None
    assert result.text == "Not recorded. Service unavailable. Try again later."


def test_capture_validation_failure_returns_short_invalid_input_feedback():
    api_client = FakeTraceFoldApiClient(
        error=TraceFoldApiError(
            "Capture input is invalid.",
            status_code=400,
            error_code="INVALID_CAPTURE_INPUT",
        )
    )
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "记一条测试",
            }
        }
    )

    assert result is not None
    assert result.text == "Not recorded. Input is invalid."


def test_blank_private_text_does_not_submit_capture():
    api_client = FakeTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "   ",
            }
        }
    )

    assert result is not None
    assert result.text == "Text is required."
    assert api_client.calls == []


def test_unknown_command_does_not_submit_capture():
    api_client = FakeTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(
        {
            "message": {
                "message_id": 10,
                "chat": {"id": 12, "type": "private"},
                "from": {"id": 13},
                "text": "/pending",
            }
        }
    )

    assert result is not None
    assert result.text == "Only /start and /help are available. Send plain text to record quickly."
    assert api_client.calls == []
