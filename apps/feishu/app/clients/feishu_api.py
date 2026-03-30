from __future__ import annotations

import json
import time
from typing import Any

import httpx


class FeishuApiError(Exception):
    pass


class FeishuApiClient:
    def __init__(
        self,
        *,
        app_id: str,
        app_secret: str,
        open_base_url: str,
        timeout_seconds: float,
        http_client: httpx.Client | None = None,
    ) -> None:
        self._owns_client = http_client is None
        self._client = http_client or httpx.Client(
            base_url=open_base_url.rstrip("/"),
            timeout=timeout_seconds,
        )
        self._app_id = app_id
        self._app_secret = app_secret
        self._tenant_access_token: str | None = None
        self._tenant_access_token_expires_at = 0.0

    def close(self) -> None:
        if self._owns_client:
            self._client.close()

    def get_tenant_access_token(self) -> str:
        now = time.time()
        if self._tenant_access_token and now < self._tenant_access_token_expires_at:
            return self._tenant_access_token

        payload = self._post_json(
            "/auth/v3/tenant_access_token/internal",
            json={
                "app_id": self._app_id,
                "app_secret": self._app_secret,
            },
        )
        token = payload.get("tenant_access_token")
        if not isinstance(token, str) or not token:
            raise FeishuApiError("Feishu API token response is invalid.")

        expire_seconds = payload.get("expire")
        ttl = int(expire_seconds) if isinstance(expire_seconds, int) else 7200
        self._tenant_access_token = token
        self._tenant_access_token_expires_at = now + max(60, ttl - 60)
        return token

    def reply_text_message(self, *, message_id: str, text: str) -> dict[str, Any]:
        access_token = self.get_tenant_access_token()
        return self._post_json(
            f"/im/v1/messages/{message_id}/reply",
            json={
                "msg_type": "text",
                "content": json.dumps({"text": text}, ensure_ascii=False),
            },
            headers={"Authorization": f"Bearer {access_token}"},
        )

    def _post_json(
        self,
        path: str,
        *,
        json: dict[str, Any] | None = None,
        headers: dict[str, str] | None = None,
    ) -> dict[str, Any]:
        try:
            response = self._client.post(path, json=json, headers=headers)
            payload = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise FeishuApiError("Feishu API request failed.") from exc

        if not response.is_success or payload.get("code") != 0:
            raise FeishuApiError("Feishu API request failed.")

        return payload
