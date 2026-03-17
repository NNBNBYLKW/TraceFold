from __future__ import annotations

from typing import Any

import httpx


class TelegramApiError(Exception):
    pass


class TelegramApiClient:
    def __init__(
        self,
        *,
        bot_token: str,
        timeout_seconds: float,
        http_client: httpx.Client | None = None,
    ) -> None:
        self._owns_client = http_client is None
        self._client = http_client or httpx.Client(
            base_url=f"https://api.telegram.org/bot{bot_token}",
            timeout=timeout_seconds,
        )

    def close(self) -> None:
        if self._owns_client:
            self._client.close()

    def get_me(self) -> dict[str, Any]:
        return self._post_json("/getMe")

    def get_updates(
        self,
        *,
        offset: int | None = None,
        timeout_seconds: int = 0,
    ) -> list[dict[str, Any]]:
        payload: dict[str, Any] = {"timeout": timeout_seconds}
        if offset is not None:
            payload["offset"] = offset
        return self._post_json("/getUpdates", json=payload)

    def send_text_message(self, chat_id: int, text: str) -> dict[str, Any]:
        return self._post_json("/sendMessage", json={"chat_id": chat_id, "text": text})

    def _post_json(
        self,
        path: str,
        *,
        json: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        try:
            response = self._client.post(path, json=json)
            payload = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise TelegramApiError("Telegram API request failed.") from exc

        if not response.is_success or not payload.get("ok", False):
            raise TelegramApiError("Telegram API request failed.")

        return payload.get("result", {})
