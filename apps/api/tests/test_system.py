from fastapi.testclient import TestClient


def test_ping(client: TestClient) -> None:
    response = client.get("/api/ping")

    assert response.status_code == 200
    assert response.json() == {"message": "pong"}


def test_healthz(client: TestClient) -> None:
    response = client.get("/api/healthz")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}