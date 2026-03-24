from __future__ import annotations

from datetime import datetime

import pytest
from fastapi.testclient import TestClient

from app.api.schemas import RuntimeStatusRead
from app.main import app


def test_runtime_status_endpoint_returns_shared_status_payload(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    checked_at = datetime(2026, 3, 23, 12, 0, 0)

    monkeypatch.setattr("app.main.init_db", lambda: None)
    monkeypatch.setattr(
        "app.api.system.get_runtime_status_read",
        lambda: RuntimeStatusRead(
            api_status="ok",
            db_status="ok",
            migration_head="20260323_0005",
            schema_version="20260323_0005",
            migration_status="ok",
            degraded_reasons=[],
            task_runtime_status="ready",
            last_checked_at=checked_at,
        ),
    )

    with TestClient(app) as client:
        response = client.get("/api/system/status")

    assert response.status_code == 200
    assert response.json() == {
        "success": True,
        "message": "Runtime status fetched.",
        "data": {
            "api_status": "ok",
            "db_status": "ok",
            "migration_head": "20260323_0005",
            "schema_version": "20260323_0005",
            "migration_status": "ok",
            "degraded_reasons": [],
            "task_runtime_status": "ready",
            "last_checked_at": "2026-03-23T12:00:00",
        },
        "meta": None,
        "error": None,
    }
