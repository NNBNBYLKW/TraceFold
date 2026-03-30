from apps.telegram.app.bot.handlers import TelegramMessageHandler


class RecordingTraceFoldApiClient:
    def __init__(self) -> None:
        self.calls: list[tuple] = []

    def submit_capture(self, *, raw_text, source_type="telegram", source_ref=None):
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


def _update(text: str) -> dict:
    return {
        "message": {
            "message_id": 9,
            "chat": {"id": 10, "type": "private"},
            "from": {"id": 11},
            "text": text,
        }
    }


def test_telegram_final_consistency_uses_capture_submit_for_plain_text_only():
    api_client = RecordingTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    handler.handle_update(_update("Lunch 25"))
    handler.handle_update(_update("/start"))
    handler.handle_update(_update("/help"))
    handler.handle_update(_update("/pending"))

    assert [call[0] for call in api_client.calls] == ["submit_capture"]


def test_telegram_final_consistency_does_not_expose_review_or_summary_commands():
    api_client = RecordingTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    responses = [
        handler.handle_update(_update("/pending")),
        handler.handle_update(_update("/confirm 12")),
        handler.handle_update(_update("/discard 12")),
        handler.handle_update(_update("/fix 12 corrected")),
        handler.handle_update(_update("/dashboard")),
        handler.handle_update(_update("/alerts")),
        handler.handle_update(_update("/status")),
        handler.handle_update(_update("/force_insert 12")),
    ]

    assert all(result is not None for result in responses)
    assert all(
        result.text == "Only /start and /help are available. Send plain text to record quickly."
        for result in responses
    )
    assert api_client.calls == []


def test_telegram_final_consistency_does_not_grow_template_or_workbench_commands():
    api_client = RecordingTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    template_result = handler.handle_update(_update("/template create"))
    workbench_result = handler.handle_update(_update("/workbench"))

    assert template_result is not None
    assert workbench_result is not None
    assert template_result.text == "Only /start and /help are available. Send plain text to record quickly."
    assert workbench_result.text == "Only /start and /help are available. Send plain text to record quickly."
    assert api_client.calls == []
    assert not hasattr(handler, "_handle_template_command")
