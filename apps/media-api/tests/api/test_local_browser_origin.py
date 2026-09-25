from __future__ import annotations

import pytest
from fastapi.testclient import TestClient


@pytest.mark.parametrize("origin", ["https://evil.example", "null"])
def test_cross_site_browser_scan_is_rejected(
    client: TestClient, origin: str
) -> None:
    response = client.post(
        "/api/inbox/scan",
        headers={"Origin": origin},
    )

    assert response.status_code == 403
    assert response.json()["error"]["code"] == "HTTP_403"
    assert response.headers["X-Correlation-ID"]


def test_cross_site_fetch_metadata_without_origin_is_rejected(client: TestClient) -> None:
    response = client.post(
        "/api/inbox/scan",
        headers={"Sec-Fetch-Site": "cross-site"},
    )

    assert response.status_code == 403


def test_local_frontend_and_non_browser_clients_can_scan(client: TestClient) -> None:
    from_frontend = client.post(
        "/api/inbox/scan",
        headers={"Origin": "http://127.0.0.1:3100"},
    )
    from_local_client = client.post("/api/inbox/scan")
    from_same_origin = client.post(
        "/api/inbox/scan",
        headers={"Origin": "http://127.0.0.1"},
    )

    assert from_frontend.status_code == 200
    assert from_local_client.status_code == 200
    assert from_same_origin.status_code == 200


def test_cross_site_read_is_rejected(client: TestClient) -> None:
    response = client.get("/health", headers={"Origin": "https://evil.example"})

    assert response.status_code == 403
    assert response.headers["X-Correlation-ID"]


def test_same_site_read_without_allowed_origin_is_rejected(client: TestClient) -> None:
    response = client.get("/health", headers={"Sec-Fetch-Site": "same-site"})

    assert response.status_code == 403


def test_untrusted_host_cannot_read_local_api(client: TestClient) -> None:
    response = client.get("/health", headers={"Host": "evil.example"})

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "INVALID_HOST"
    assert response.headers["X-Correlation-ID"]


def test_ipv6_loopback_host_can_read_local_api(client: TestClient) -> None:
    response = client.get("/health", headers={"Host": "[::1]:8100"})

    assert response.status_code == 200
