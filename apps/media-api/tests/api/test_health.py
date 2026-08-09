from fastapi.testclient import TestClient


def test_health_uses_media_api_only(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"service": "tracefold-media-api", "status": "ok"}
