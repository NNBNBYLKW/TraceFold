import logging

from apps.telegram.app.bot.handlers import TelegramMessageHandler


class FakeTraceFoldApiClient:
    def get_pending_list(self, *, limit=5, offset=0, status="pending"):
        return {"items": []}


def test_pending_command_emits_minimal_observability_log(caplog):
    handler = TelegramMessageHandler(tracefold_api=FakeTraceFoldApiClient())

    with caplog.at_level(logging.INFO, logger="tracefold.telegram.adapter"):
        handler.handle_update(
            {
                "message": {
                    "message_id": 9,
                    "chat": {"id": 10, "type": "private"},
                    "from": {"id": 11},
                    "text": "/pending",
                }
            }
        )

    assert "command=/pending" in caplog.text
    assert "chat_id=10" in caplog.text
    assert "message_id=9" in caplog.text
    assert "endpoint=pending_list" in caplog.text
    assert "outcome=success" in caplog.text
