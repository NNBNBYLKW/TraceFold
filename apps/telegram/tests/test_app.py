from apps.telegram.app.bot.app import TelegramAdapterApp
from apps.telegram.app.bot.handlers import TelegramMessageHandler
from apps.telegram.app.core.config import TelegramAdapterSettings


class FakeTraceFoldApiClient:
    def __init__(self):
        self.closed = False

    def get_health_status(self):
        return {"status": "ok"}

    def get_pending_list(self, *, limit=5, offset=0, status="pending"):
        return {"items": []}

    def get_pending_detail(self, pending_id):
        return {"id": pending_id, "status": "pending", "target_domain": "expense"}

    def get_dashboard(self):
        return {
            "pending_summary": {"open_count": 1},
            "expense_summary": {"count": 2},
            "knowledge_summary": {"count": 3},
            "health_summary": {"count": 4},
        }

    def get_alerts(self):
        return {"items": []}

    def get_status(self):
        return {"status": "ok"}

    def confirm_pending(self, pending_id):
        return {"pending_item_id": pending_id, "status": "confirmed"}

    def discard_pending(self, pending_id):
        return {"pending_item_id": pending_id, "status": "discarded"}

    def fix_pending(self, pending_id, correction_text):
        return {"pending_item_id": pending_id, "status": "pending"}

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

    def close(self):
        self.closed = True


class FakeTelegramApiClient:
    def __init__(self):
        self.sent_messages = []
        self.closed = False
        self.updates = []

    def get_me(self):
        return {"id": 123, "username": "tracefold_bot"}

    def get_updates(self, *, offset=None, timeout_seconds=0):
        return list(self.updates)

    def send_text_message(self, chat_id, text):
        self.sent_messages.append((chat_id, text))
        return {"message_id": 1}

    def close(self):
        self.closed = True


def test_adapter_app_wires_dependencies_and_handles_basic_update():
    settings = TelegramAdapterSettings.model_validate(
        {
            "TRACEFOLD_TELEGRAM_BOT_TOKEN": "test-token",
            "TRACEFOLD_TELEGRAM_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS": 5,
            "TRACEFOLD_TELEGRAM_DEBUG": False,
            "TRACEFOLD_TELEGRAM_LOG_ENABLED": True,
        }
    )
    tracefold_api = FakeTraceFoldApiClient()
    telegram_api = FakeTelegramApiClient()
    app = TelegramAdapterApp(
        settings=settings,
        tracefold_api=tracefold_api,
        telegram_api=telegram_api,
        handler=TelegramMessageHandler(tracefold_api=tracefold_api),
    )
    telegram_api.updates = [
        {
            "update_id": 10,
            "message": {
                "chat": {"id": 42},
                "from": {"id": 7},
                "text": "/help",
            },
        }
    ]

    summary = app.startup_summary()
    probe = app.probe_dependencies()
    next_offset = app.process_updates_once()
    app.close()

    assert summary["api_base_url"] == "http://localhost:8000/api"
    assert probe["tracefold_api"] == {"status": "ok"}
    assert probe["telegram_identity"]["username"] == "tracefold_bot"
    assert next_offset == 11
    assert telegram_api.sent_messages
    assert tracefold_api.closed is True
    assert telegram_api.closed is True
