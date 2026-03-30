from fastapi.testclient import TestClient

from apps.feishu.app.bot.app import FeishuAdapterApp
from apps.feishu.app.bot.handlers import FeishuMessageHandler
from apps.feishu.app.core.config import FeishuAdapterSettings
from apps.feishu.app.main import create_app


class FakeTraceFoldApiClient:
    def __init__(self):
        self.closed = False

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

    def close(self):
        self.closed = True


class FakeFeishuApiClient:
    def __init__(self):
        self.closed = False
        self.replies = []

    def reply_text_message(self, *, message_id: str, text: str):
        self.replies.append((message_id, text))
        return {"code": 0, "msg": "success"}

    def close(self):
        self.closed = True


def _settings() -> FeishuAdapterSettings:
    return FeishuAdapterSettings.model_validate(
        {
            "TRACEFOLD_FEISHU_APP_ID": "app-id",
            "TRACEFOLD_FEISHU_APP_SECRET": "secret",
            "TRACEFOLD_FEISHU_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_FEISHU_OPEN_BASE_URL": "https://open.feishu.cn/open-apis",
            "TRACEFOLD_FEISHU_TIMEOUT_SECONDS": 5,
            "TRACEFOLD_FEISHU_DEBUG": False,
            "TRACEFOLD_FEISHU_LOG_ENABLED": True,
        }
    )


def test_url_verification_returns_challenge_without_replying():
    tracefold_api = FakeTraceFoldApiClient()
    feishu_api = FakeFeishuApiClient()
    runtime = FeishuAdapterApp(
        settings=_settings(),
        tracefold_api=tracefold_api,
        feishu_api=feishu_api,
        handler=FeishuMessageHandler(tracefold_api=tracefold_api),
    )
    app = create_app(runtime)

    with TestClient(app) as client:
        response = client.post(
            "/feishu/events",
            json={"type": "url_verification", "challenge": "verify-me"},
        )

    assert response.status_code == 200
    assert response.json() == {"challenge": "verify-me"}
    assert feishu_api.replies == []


def test_text_event_replies_with_capture_first_feedback():
    tracefold_api = FakeTraceFoldApiClient()
    feishu_api = FakeFeishuApiClient()
    runtime = FeishuAdapterApp(
        settings=_settings(),
        tracefold_api=tracefold_api,
        feishu_api=feishu_api,
        handler=FeishuMessageHandler(tracefold_api=tracefold_api),
    )
    app = create_app(runtime)

    payload = {
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

    with TestClient(app) as client:
        response = client.post("/feishu/events", json=payload)

    assert response.status_code == 200
    assert response.json() == {"code": 0}
    assert feishu_api.replies == [
        ("om_456", "Captured first. Pending review created. You can send the next text now.")
    ]
    assert tracefold_api.closed is True
    assert feishu_api.closed is True


def test_non_message_event_returns_ok_without_replying():
    tracefold_api = FakeTraceFoldApiClient()
    feishu_api = FakeFeishuApiClient()
    runtime = FeishuAdapterApp(
        settings=_settings(),
        tracefold_api=tracefold_api,
        feishu_api=feishu_api,
        handler=FeishuMessageHandler(tracefold_api=tracefold_api),
    )
    app = create_app(runtime)

    payload = {
        "schema": "2.0",
        "header": {"event_type": "im.chat.member.bot.added_v1"},
        "event": {},
    }

    with TestClient(app) as client:
        response = client.post("/feishu/events", json=payload)

    assert response.status_code == 200
    assert response.json() == {"code": 0}
    assert feishu_api.replies == []
