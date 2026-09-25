from __future__ import annotations

import pytest
from fastapi.testclient import TestClient


@pytest.mark.parametrize("origin", ["https://evil.example", "null"])
def test_cross_site_browser_write_is_rejected_before_alert_handler(
    client: TestClient, origin: str
) -> None:
    response = client.post(
        "/api/alerts/1/acknowledge",
        headers={"Origin": origin},
    )

    assert response.status_code == 403


def test_cross_site_fetch_metadata_without_origin_is_rejected(client: TestClient) -> None:
    response = client.post(
        "/api/alerts/1/acknowledge",
        headers={"Sec-Fetch-Site": "cross-site"},
    )

    assert response.status_code == 403


def test_local_frontend_and_non_browser_clients_keep_write_access(client: TestClient) -> None:
    from_frontend = client.post(
        "/api/alerts/1/acknowledge",
        headers={"Origin": "http://127.0.0.1:3000"},
    )
    from_local_client = client.post("/api/alerts/1/acknowledge")
    from_same_origin = client.post(
        "/api/alerts/1/acknowledge",
        headers={"Origin": "http://127.0.0.1"},
    )

    assert from_frontend.status_code != 403
    assert from_local_client.status_code != 403
    assert from_same_origin.status_code != 403


def test_cross_site_read_is_rejected(client: TestClient) -> None:
    response = client.get("/api/healthz", headers={"Origin": "https://evil.example"})

    assert response.status_code == 403
    assert response.json()["error"]["code"] == "FORBIDDEN"


def test_untrusted_host_cannot_read_local_api(client: TestClient) -> None:
    response = client.get("/api/healthz", headers={"Host": "evil.example"})

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "INVALID_HOST"


def test_ipv6_loopback_host_can_read_local_api(client: TestClient) -> None:
    response = client.get("/api/healthz", headers={"Host": "[::1]:8000"})

    assert response.status_code == 200
