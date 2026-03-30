import logging

from apps.feishu.app.bot.handlers import FeishuMessageHandler


class FakeTraceFoldApiClient:
    def submit_capture(self, *, raw_text, source_type="feishu", source_ref=None):
        return {
            "capture_created": True,
            "capture_id": 1,
            "status": "pending",
            "route": "pending",
            "target_domain": "expense",
            "pending_item_id": 2,
            "formal_record_id": None,
        }


def test_plain_text_capture_emits_minimal_observability_log(caplog):
    handler = FeishuMessageHandler(tracefold_api=FakeTraceFoldApiClient())

    with caplog.at_level(logging.INFO, logger="tracefold.feishu.adapter"):
        handler.handle_event(
            {
                "schema": "2.0",
                "header": {"event_type": "im.message.receive_v1"},
                "event": {
                    "sender": {"sender_id": {"open_id": "ou_123"}},
                    "message": {
                        "message_id": "om_456",
                        "chat_id": "oc_789",
                        "chat_type": "p2p",
                        "message_type": "text",
                        "content": "{\"text\":\"Lunch 25\"}",
                    },
                },
            }
        )

    assert "command=plain_text" in caplog.text
    assert "chat_id=oc_789" in caplog.text
    assert "message_id=om_456" in caplog.text
    assert "endpoint=capture_submit" in caplog.text
    assert "outcome=success" in caplog.text
