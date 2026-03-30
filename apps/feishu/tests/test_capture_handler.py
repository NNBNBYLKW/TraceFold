import json

from apps.feishu.app.bot.handlers import FeishuMessageHandler
from apps.feishu.app.clients.tracefold_api import TraceFoldApiError


class FakeTraceFoldApiClient:
    def __init__(self, result=None, error=None):
        self.result = result
        self.error = error
        self.calls = []

    def submit_capture(self, *, raw_text, source_type="feishu", source_ref=None):
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


def _event(text: str, *, message_type: str = "text") -> dict:
    return {
        "schema": "2.0",
        "header": {"event_type": "im.message.receive_v1"},
        "event": {
            "sender": {"sender_id": {"open_id": "ou_123"}},
            "message": {
                "message_id": "om_456",
                "chat_id": "oc_789",
                "chat_type": "p2p",
                "message_type": message_type,
                "content": json.dumps({"text": text}, ensure_ascii=False),
            },
        },
    }


def test_plain_text_submits_capture_and_returns_pending_feedback():
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
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("Lunch 25"))

    assert result is not None
    assert result.text == "Captured first. Pending review created. You can send the next text now."
    assert api_client.calls == [
        {
            "raw_text": "Lunch 25",
            "source_type": "feishu",
            "source_ref": "chat:oc_789:message:om_456:user:ou_123",
        }
    ]


def test_start_and_help_guidance_remain_minimal():
    handler = FeishuMessageHandler()

    start_result = handler.handle_event(_event("start"))
    help_result = handler.handle_event(_event("help"))

    assert start_result is not None
    assert help_result is not None
    assert "quick capture is ready" in start_result.text.lower()
    assert "capture record first" in start_result.text.lower()
    assert "send plain text" in help_result.text.lower()
    assert "tracefold web" in help_result.text.lower()


def test_capture_failure_returns_short_unavailable_feedback():
    api_client = FakeTraceFoldApiClient(
        error=TraceFoldApiError("TraceFold API is unavailable.")
    )
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("Record this"))

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
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("Record this"))

    assert result is not None
    assert result.text == "Not recorded. Input is invalid."


def test_blank_text_does_not_submit_capture():
    api_client = FakeTraceFoldApiClient()
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("   "))

    assert result is not None
    assert result.text == "Text is required."
    assert api_client.calls == []


def test_unknown_slash_command_does_not_submit_capture():
    api_client = FakeTraceFoldApiClient()
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("/pending"))

    assert result is not None
    assert result.text == "Only start/help guidance is available. Send plain text to record quickly."
    assert api_client.calls == []


def test_non_text_message_returns_unsupported_feedback():
    api_client = FakeTraceFoldApiClient()
    handler = FeishuMessageHandler(tracefold_api=api_client)

    result = handler.handle_event(_event("ignored", message_type="image"))

    assert result is not None
    assert result.text == "Only simple text messages are supported."
    assert api_client.calls == []
