import logging

from apps.telegram.app.bot.handlers import TelegramMessageHandler


class FakeTraceFoldApiClient:
    def submit_capture(self, *, raw_text, source_type="telegram", source_ref=None):
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
    handler = TelegramMessageHandler(tracefold_api=FakeTraceFoldApiClient())

    with caplog.at_level(logging.INFO, logger="tracefold.telegram.adapter"):
        handler.handle_update(
            {
                "message": {
                    "message_id": 9,
                    "chat": {"id": 10, "type": "private"},
                    "from": {"id": 11},
                    "text": "Lunch 25",
                }
            }
        )

    assert "command=plain_text" in caplog.text
    assert "chat_id=10" in caplog.text
    assert "message_id=9" in caplog.text
    assert "endpoint=capture_submit" in caplog.text
    assert "outcome=success" in caplog.text
