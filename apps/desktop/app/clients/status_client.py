from __future__ import annotations

from typing import Any

import httpx
from pydantic import BaseModel


class StatusEnvelope(BaseModel):
    success: bool
    message: str | None = None
    data: Any = None
    meta: dict[str, Any] | None = None
    error: dict[str, Any] | None = None


class DesktopStatusClientError(Exception):
    def __init__(self, message: str, *, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


class TraceFoldStatusClient:
    @classmethod
    def create(
        cls,
        *,
        base_url: str,
        transport: httpx.BaseTransport | None = None,
        timeout_seconds: float = 5.0,
    ) -> "TraceFoldStatusClient":
        http_client = httpx.Client(
            base_url=base_url.rstrip("/"),
            timeout=timeout_seconds,
            transport=transport,
        )
        return cls(
            base_url=base_url,
            timeout_seconds=timeout_seconds,
            http_client=http_client,
        )

    def __init__(
        self,
        *,
        base_url: str,
        timeout_seconds: float,
        http_client: httpx.Client | None = None,
    ) -> None:
        self._owns_client = http_client is None
        self._client = http_client or httpx.Client(
            base_url=base_url.rstrip("/"),
            timeout=timeout_seconds,
        )

    def close(self) -> None:
        if self._owns_client:
            self._client.close()

    def get_status(self) -> dict[str, Any]:
        try:
            response = self._client.get("/healthz")
        except httpx.HTTPError as exc:
            raise DesktopStatusClientError("TraceFold API is unavailable.") from exc

        try:
            payload = StatusEnvelope.model_validate(response.json())
        except ValueError as exc:
            raise DesktopStatusClientError(
                "TraceFold API returned an invalid response.",
                status_code=response.status_code,
            ) from exc

        if not response.is_success or not payload.success:
            raise DesktopStatusClientError(
                payload.message or "Status check failed.",
                status_code=response.status_code,
            )

        if isinstance(payload.data, dict):
            return payload.data
        return {"status": "unknown"}
