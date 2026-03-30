import httpx
import pytest

from apps.feishu.app.clients.tracefold_api import TraceFoldApiClient, TraceFoldApiError


def test_tracefold_api_client_returns_data_from_success_envelope():
    transport = httpx.MockTransport(
        lambda request: httpx.Response(
            200,
            json={
                "success": True,
                "message": "ok",
                "data": {"status": "ok"},
                "error": None,
                "meta": None,
            },
        )
    )
    client = httpx.Client(transport=transport, base_url="http://testserver/api")
    api_client = TraceFoldApiClient(
        base_url="http://testserver/api",
        timeout_seconds=5,
        http_client=client,
    )

    result = api_client.get_health_status()

    assert result == {"status": "ok"}


def test_tracefold_api_client_maps_error_envelope_to_adapter_error():
    transport = httpx.MockTransport(
        lambda request: httpx.Response(
            404,
            json={
                "success": False,
                "message": "Capture item not found.",
                "data": None,
                "error": {"code": "CAPTURE_ITEM_NOT_FOUND"},
                "meta": None,
            },
        )
    )
    client = httpx.Client(transport=transport, base_url="http://testserver/api")
    api_client = TraceFoldApiClient(
        base_url="http://testserver/api",
        timeout_seconds=5,
        http_client=client,
    )

    with pytest.raises(TraceFoldApiError) as exc_info:
        api_client.request("GET", "/capture/999")

    assert exc_info.value.status_code == 404
    assert exc_info.value.error_code == "CAPTURE_ITEM_NOT_FOUND"
