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

    def get_pending_list(self, *, limit=5, offset=0, status="pending"):
        self.calls.append(("get_pending_list", limit, offset, status))
        return {"items": []}

    def get_pending_detail(self, pending_id):
        self.calls.append(("get_pending_detail", pending_id))
        return {"id": pending_id, "status": "pending", "target_domain": "expense"}

    def confirm_pending(self, pending_id):
        self.calls.append(("confirm_pending", pending_id))
        return {"pending_item_id": pending_id, "status": "confirmed"}

    def discard_pending(self, pending_id):
        self.calls.append(("discard_pending", pending_id))
        return {"pending_item_id": pending_id, "status": "discarded"}

    def fix_pending(self, pending_id, correction_text):
        self.calls.append(("fix_pending", pending_id, correction_text))
        return {"pending_item_id": pending_id, "status": "pending"}

    def get_dashboard(self):
        self.calls.append(("get_dashboard",))
        return {
            "pending_summary": {"open_count": 1},
            "expense_summary": {"count": 2},
            "knowledge_summary": {"count": 3},
            "health_summary": {"count": 4},
        }

    def get_alerts(self):
        self.calls.append(("get_alerts",))
        return {"items": []}

    def get_status(self):
        self.calls.append(("get_status",))
        return {"status": "ok"}


def _update(text: str) -> dict:
    return {
        "message": {
            "message_id": 9,
            "chat": {"id": 10, "type": "private"},
            "from": {"id": 11},
            "text": text,
        }
    }


def test_telegram_final_consistency_paths_use_unified_api_surface():
    api_client = RecordingTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    handler.handle_update(_update("Lunch 25"))
    handler.handle_update(_update("/pending"))
    handler.handle_update(_update("/pending 12"))
    handler.handle_update(_update("/confirm 12"))
    handler.handle_update(_update("/discard 12"))
    handler.handle_update(_update("/fix 12 Lunch 28"))
    handler.handle_update(_update("/dashboard"))
    handler.handle_update(_update("/alerts"))
    handler.handle_update(_update("/status"))

    assert [call[0] for call in api_client.calls] == [
        "submit_capture",
        "get_pending_list",
        "get_pending_detail",
        "confirm_pending",
        "discard_pending",
        "fix_pending",
        "get_dashboard",
        "get_alerts",
        "get_status",
    ]


def test_telegram_final_consistency_does_not_expose_force_insert_path():
    api_client = RecordingTraceFoldApiClient()
    handler = TelegramMessageHandler(tracefold_api=api_client)

    result = handler.handle_update(_update("/force_insert 12"))

    assert result is not None
    assert result.text == "This command is not available."
    assert api_client.calls == []
