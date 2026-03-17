from fastapi.testclient import TestClient

from app.main import app


def test_ping(monkeypatch) -> None:
    monkeypatch.setattr("app.main.init_db", lambda: None)
    with TestClient(app) as client:
        response = client.get("/api/ping")

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "Ping OK.",
        "data": {"message": "pong"},
        "meta": None,
        "error": None,
    }


def test_healthz(monkeypatch) -> None:
    monkeypatch.setattr("app.main.init_db", lambda: None)
    with TestClient(app) as client:
        response = client.get("/api/healthz")

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "Health check OK.",
        "data": {"status": "ok"},
        "meta": None,
        "error": None,
    }
