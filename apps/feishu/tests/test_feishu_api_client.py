import json

import httpx

from apps.feishu.app.clients.feishu_api import FeishuApiClient


def test_feishu_api_client_fetches_token_and_replies_to_message():
    requests: list[tuple[str, dict | None, dict | None]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        payload = json.loads(request.content.decode()) if request.content else None
        requests.append((request.url.path, payload, dict(request.headers)))

        if request.url.path.endswith("/auth/v3/tenant_access_token/internal"):
            return httpx.Response(
                200,
                json={
                    "code": 0,
                    "msg": "success",
                    "tenant_access_token": "tenant-token",
                    "expire": 7200,
                },
            )

        return httpx.Response(
            200,
            json={
                "code": 0,
                "msg": "success",
                "data": {"message_id": "om_reply"},
            },
        )

    client = httpx.Client(
        transport=httpx.MockTransport(handler),
        base_url="https://open.feishu.cn/open-apis",
    )
    api_client = FeishuApiClient(
        app_id="app-id",
        app_secret="app-secret",
        open_base_url="https://open.feishu.cn/open-apis",
        timeout_seconds=5,
        http_client=client,
    )

    result = api_client.reply_text_message(message_id="om_123", text="Captured first.")

    assert result["code"] == 0
    assert requests[0][0].endswith("/auth/v3/tenant_access_token/internal")
    assert requests[1][0].endswith("/im/v1/messages/om_123/reply")
    assert requests[1][1]["msg_type"] == "text"
    assert json.loads(requests[1][1]["content"])["text"] == "Captured first."
    assert requests[1][2]["authorization"] == "Bearer tenant-token"
