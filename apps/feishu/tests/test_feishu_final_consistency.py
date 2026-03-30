import json

from apps.feishu.app.bot.handlers import FeishuMessageHandler


class RecordingTraceFoldApiClient:
    def __init__(self) -> None:
        self.calls: list[tuple] = []

    def submit_capture(self, *, raw_text, source_type="feishu", source_ref=None):
        self.calls.append(("submit_capture", raw_text, source_type, source_ref))
        return {
            "capture_created": True,
            "capture_id": 1,
            "status": "pending",
            "route": "pending",
            "target_domain": "expense",
            "pending_item_id": 12,
            "formal_record_id": None,
        }


def _event(text: str) -> dict:
    return {
        "schema": "2.0",
        "header": {"event_type": "im.message.receive_v1"},
        "event": {
            "sender": {"sender_id": {"open_id": "ou_123"}},
            "message": {
                "message_id": "om_456",
                "chat_id": "oc_789",
                "chat_type": "p2p",
                "message_type": "text",
                "content": json.dumps({"text": text}, ensure_ascii=False),
            },
        },
    }


def test_feishu_final_consistency_uses_capture_submit_for_plain_text_only():
    api_client = RecordingTraceFoldApiClient()
    handler = FeishuMessageHandler(tracefold_api=api_client)

    handler.handle_event(_event("Lunch 25"))
    handler.handle_event(_event("start"))
    handler.handle_event(_event("help"))
    handler.handle_event(_event("/pending"))

    assert [call[0] for call in api_client.calls] == ["submit_capture"]


def test_feishu_final_consistency_does_not_expose_review_or_workflow_commands():
    api_client = RecordingTraceFoldApiClient()
    handler = FeishuMessageHandler(tracefold_api=api_client)

    responses = [
        handler.handle_event(_event("/pending")),
        handler.handle_event(_event("/confirm 12")),
        handler.handle_event(_event("/discard 12")),
        handler.handle_event(_event("/fix 12 corrected")),
        handler.handle_event(_event("/dashboard")),
        handler.handle_event(_event("/alerts")),
        handler.handle_event(_event("/status")),
        handler.handle_event(_event("/force_insert 12")),
    ]

    assert all(result is not None for result in responses)
    assert all(
        result.text == "Only start/help guidance is available. Send plain text to record quickly."
        for result in responses
    )
    assert api_client.calls == []
