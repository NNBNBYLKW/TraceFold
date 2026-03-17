from __future__ import annotations

from typing import Any

import httpx
from pydantic import BaseModel


class TraceFoldApiEnvelope(BaseModel):
    success: bool
    message: str | None = None
    data: Any = None
    meta: dict[str, Any] | None = None
    error: dict[str, Any] | None = None


class TraceFoldApiError(Exception):
    def __init__(
        self,
        message: str,
        *,
        status_code: int | None = None,
        error_code: str | None = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.error_code = error_code
        self.code = error_code


class TraceFoldApiClient:
    @classmethod
    def create(
        cls,
        *,
        base_url: str,
        timeout_seconds: float,
        transport: httpx.BaseTransport | None = None,
    ) -> "TraceFoldApiClient":
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

    def get_health_status(self) -> dict[str, Any]:
        return self.request("GET", "/healthz")

    def get_system_health(self) -> dict[str, Any]:
        return self._request("GET", "/healthz")

    def get_pending_list(
        self,
        *,
        limit: int = 5,
        offset: int = 0,
        status: str = "pending",
    ) -> dict[str, Any] | list[dict[str, Any]]:
        return self.request(
            "GET",
            "/pending",
            params={"limit": limit, "offset": offset, "status": status},
        )

    def get_pending_detail(self, pending_id: int) -> dict[str, Any]:
        return self.request("GET", f"/pending/{pending_id}")

    def get_dashboard(self) -> dict[str, Any]:
        return self.request("GET", "/dashboard")

    def get_alerts(self) -> dict[str, Any] | list[dict[str, Any]]:
        return self.request("GET", "/alerts")

    def get_status(self) -> dict[str, Any]:
        return self.request("GET", "/healthz")

    def confirm_pending(self, pending_id: int) -> dict[str, Any]:
        return self.request("POST", f"/pending/{pending_id}/confirm", json={})

    def discard_pending(self, pending_id: int) -> dict[str, Any]:
        return self.request("POST", f"/pending/{pending_id}/discard", json={})

    def fix_pending(self, pending_id: int, correction_text: str) -> dict[str, Any]:
        return self.request(
            "POST",
            f"/pending/{pending_id}/fix",
            json={"correction_text": correction_text},
        )

    def submit_capture(
        self,
        *,
        raw_text: str,
        source_type: str = "telegram",
        source_ref: str | None = None,
    ) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "raw_text": raw_text,
            "source_type": source_type,
        }
        if source_ref:
            payload["source_ref"] = source_ref
        return self.request("POST", "/capture", json=payload)

    def request(
        self,
        method: str,
        path: str,
        *,
        json: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
    ) -> Any:
        payload = self._request(method, path, json=json, params=params)
        return payload.get("data")

    def _request(
        self,
        method: str,
        path: str,
        *,
        json: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        try:
            response = self._client.request(method, path, json=json, params=params)
        except httpx.HTTPError as exc:
            raise TraceFoldApiError("TraceFold API is unavailable.") from exc

        try:
            payload = TraceFoldApiEnvelope.model_validate(response.json())
        except ValueError as exc:
            raise TraceFoldApiError(
                "TraceFold API returned an invalid response.",
                status_code=response.status_code,
            ) from exc

        if not response.is_success or not payload.success:
            error_code = None
            if payload.error:
                error_code = payload.error.get("code")

            raise TraceFoldApiError(
                payload.message or "TraceFold API request failed.",
                status_code=response.status_code,
                error_code=error_code,
            )

        return payload.model_dump()
