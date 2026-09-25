import json

import pytest
from fastapi.testclient import TestClient

from apps.feishu.app.bot.app import FeishuAdapterApp
from apps.feishu.app.bot.handlers import FeishuMessageHandler
from apps.feishu.app.core.config import FeishuAdapterSettings
from apps.feishu.app.main import create_app


class FakeTraceFoldApiClient:
    def __init__(self):
        self.closed = False
        self.calls = []

    def submit_capture(self, *, raw_text, source_type="feishu", source_ref=None):
        self.calls.append((raw_text, source_type, source_ref))
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
            "TRACEFOLD_FEISHU_VERIFICATION_TOKEN": "verification-token",
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
            json={"type": "url_verification", "token": "verification-token", "challenge": "verify-me"},
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
        "header": {"event_type": "im.message.receive_v1", "token": "verification-token"},
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
    assert tracefold_api.calls == [("Lunch 25", "feishu", "chat:oc_789:message:om_456:user:ou_123")]
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
        "header": {"event_type": "im.chat.member.bot.added_v1", "token": "verification-token"},
        "event": {},
    }

    with TestClient(app) as client:
        response = client.post("/feishu/events", json=payload)

    assert response.status_code == 200
    assert response.json() == {"code": 0}
    assert feishu_api.replies == []


@pytest.mark.parametrize("token", [None, "wrong-token", "验证令牌", "\ud800"])
def test_url_verification_rejects_untrusted_token(token):
    tracefold_api = FakeTraceFoldApiClient()
    feishu_api = FakeFeishuApiClient()
    runtime = FeishuAdapterApp(
        settings=_settings(),
        tracefold_api=tracefold_api,
        feishu_api=feishu_api,
    )
    payload = {"type": "url_verification", "challenge": "verify-me"}
    if token is not None:
        payload["token"] = token

    with TestClient(create_app(runtime)) as client:
        response = client.post(
            "/feishu/events",
            content=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )

    assert response.status_code == 403
    assert tracefold_api.calls == []
    assert feishu_api.replies == []


@pytest.mark.parametrize("token", [None, "wrong-token", "验证令牌", "\ud800"])
def test_message_event_rejects_untrusted_token_before_side_effects(token):
    tracefold_api = FakeTraceFoldApiClient()
    feishu_api = FakeFeishuApiClient()
    runtime = FeishuAdapterApp(
        settings=_settings(),
        tracefold_api=tracefold_api,
        feishu_api=feishu_api,
    )
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
                "content": '{"text":"forged capture"}',
            },
        },
    }
    if token is not None:
        payload["header"]["token"] = token

    with TestClient(create_app(runtime)) as client:
        response = client.post(
            "/feishu/events",
            content=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )

    assert response.status_code == 403
    assert tracefold_api.calls == []
    assert feishu_api.replies == []
