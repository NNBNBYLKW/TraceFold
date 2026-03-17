from __future__ import annotations

import httpx
import pytest

from app.clients.tracefold_api import TraceFoldApiClient, TraceFoldApiError


def test_tracefold_api_client_returns_json_payload() -> None:
    client = TraceFoldApiClient.create(
        base_url="http://tracefold.test/api",
        timeout_seconds=5.0,
        transport=httpx.MockTransport(
            lambda request: httpx.Response(
                200,
                json={
                    "success": True,
                    "message": "Health check OK.",
                    "data": {"status": "ok"},
                    "meta": None,
                    "error": None,
                },
            )
        ),
    )

    payload = client.get_system_health()

    assert payload["success"] is True
    assert payload["data"]["status"] == "ok"
    client.close()


def test_tracefold_api_client_maps_error_envelope() -> None:
    client = TraceFoldApiClient.create(
        base_url="http://tracefold.test/api",
        timeout_seconds=5.0,
        transport=httpx.MockTransport(
            lambda request: httpx.Response(
                404,
                json={
                    "success": False,
                    "message": "Pending item missing.",
                    "data": None,
                    "meta": None,
                    "error": {"code": "PENDING_ITEM_NOT_FOUND", "details": None},
                },
            )
        ),
    )

    with pytest.raises(TraceFoldApiError) as exc_info:
        client._request("GET", "/pending/1")

    assert exc_info.value.status_code == 404
    assert exc_info.value.code == "PENDING_ITEM_NOT_FOUND"
    client.close()
