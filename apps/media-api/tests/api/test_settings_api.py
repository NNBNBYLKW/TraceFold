from __future__ import annotations

from fastapi.testclient import TestClient


def test_settings_api_persists_runtime_and_processor_overrides(
    client: TestClient,
) -> None:
    initial = client.get("/api/settings")
    updated = client.patch(
        "/api/settings",
        json={
            "scan_interval_seconds": 45,
            "automatic_scan_enabled": True,
            "processors": [
                {
                    "processor_name": "mp4_m4a_merge",
                    "enabled": False,
                    "auto_run": False,
                }
            ],
        },
    )
    reloaded = client.get("/api/settings")

    assert initial.status_code == 200
    assert "database_path" in initial.json()
    assert initial.json()["automatic_scan_enabled"] is False
    assert initial.json()["processors"] == [
        {
            "processor_name": "mp4_m4a_merge",
            "enabled": True,
            "auto_run": True,
        }
    ]
    assert updated.status_code == 200
    assert updated.json()["scan_interval_seconds"] == 45
    assert updated.json()["automatic_scan_enabled"] is True
    assert updated.json()["processors"] == [
        {
            "processor_name": "mp4_m4a_merge",
            "enabled": False,
            "auto_run": False,
        }
    ]
    assert reloaded.json() == updated.json()


def test_database_path_is_read_only(client: TestClient) -> None:
    response = client.patch(
        "/api/settings",
        json={"database_path": "other.sqlite3"},
    )

    assert response.status_code == 422


def test_system_status_exposes_safe_diagnostics(client: TestClient) -> None:
    response = client.get("/api/system/status")

    assert response.status_code == 200
    assert set(response.json()) == {
        "directories_writable",
        "ffmpeg_available",
        "ffprobe_available",
        "last_scan_at",
        "queued_jobs",
        "recoverable_operations",
        "recovery_in_progress",
        "ingest_enabled",
    }
    assert response.json()["directories_writable"] is True


def test_cors_allows_default_local_frontends_only(client: TestClient) -> None:
    allowed = client.options(
        "/api/settings",
        headers={
            "Origin": "http://127.0.0.1:3100",
            "Access-Control-Request-Method": "GET",
        },
    )
    rejected = client.options(
        "/api/settings",
        headers={
            "Origin": "https://example.com",
            "Access-Control-Request-Method": "GET",
        },
    )

    assert allowed.status_code == 200
    assert allowed.headers["access-control-allow-origin"] == "http://127.0.0.1:3100"
    assert "access-control-allow-origin" not in rejected.headers


def test_manual_scan_updates_system_last_scan_time(client: TestClient) -> None:
    before = client.get("/api/system/status")
    scanned = client.post("/api/inbox/scan")
    after = client.get("/api/system/status")

    assert before.json()["last_scan_at"] is None
    assert scanned.status_code == 200
    assert after.json()["last_scan_at"] is not None
