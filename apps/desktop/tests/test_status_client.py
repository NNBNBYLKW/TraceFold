import httpx
import pytest

from apps.desktop.app.clients.status_client import (
    DesktopStatusClientError,
    TraceFoldStatusClient,
)


def test_status_client_returns_status_payload():
    client = TraceFoldStatusClient.create(
        base_url="http://tracefold.test/api",
        timeout_seconds=5.0,
        transport=httpx.MockTransport(
            lambda request: httpx.Response(
                200,
                json={
                    "success": True,
                    "message": "OK",
                    "data": {"status": "ok"},
                    "meta": None,
                    "error": None,
                },
            )
        ),
    )

    payload = client.get_status()

    assert payload == {"status": "ok"}


def test_status_client_maps_unavailable_error():
    client = TraceFoldStatusClient.create(
        base_url="http://tracefold.test/api",
        timeout_seconds=5.0,
        transport=httpx.MockTransport(lambda request: (_ for _ in ()).throw(httpx.ConnectError("boom"))),
    )

    with pytest.raises(DesktopStatusClientError) as exc_info:
        client.get_status()

    assert str(exc_info.value) == "TraceFold API is unavailable."


def test_status_client_maps_invalid_response_error():
    client = TraceFoldStatusClient.create(
        base_url="http://tracefold.test/api",
        timeout_seconds=5.0,
        transport=httpx.MockTransport(lambda request: httpx.Response(200, content=b"not-json")),
    )

    with pytest.raises(DesktopStatusClientError) as exc_info:
        client.get_status()

    assert str(exc_info.value) == "TraceFold API returned an invalid response."
