from __future__ import annotations

from collections.abc import Generator
from contextlib import contextmanager
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.main import app


def _load_models() -> None:
    import app.domains.alerts.models  # noqa: F401
    import app.domains.ai_derivations.models  # noqa: F401
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401
    import app.domains.workbench.models  # noqa: F401


def _build_testing_session_local(database_path: Path):
    _load_models()
    engine = create_engine(
        f"sqlite:///{database_path}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    testing_session_local = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )
    Base.metadata.create_all(bind=engine)
    return engine, testing_session_local


@contextmanager
def _api_client(
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        db = testing_session_local()
        try:
            yield db
        finally:
            db.close()

    monkeypatch.setattr("app.main.init_db", lambda: None)
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_local_operability_read_exposes_sqlite_backup_and_transfer_context(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "tracefold-demo-operability.db"
    engine, testing_session_local = _build_testing_session_local(db_path)
    try:
        with _api_client(testing_session_local, monkeypatch) as client:
            response = client.get("/api/system/local-operability")

        assert response.status_code == 200
        payload = response.json()
        assert payload["success"] is True
        assert payload["message"] == "Local operability status fetched."
        data = payload["data"]
        assert data["database_path"].endswith("tracefold-demo-operability.db")
        assert data["database_exists"] is True
        assert data["backup_directory"].endswith("backups")
        assert data["transfer_directory"].endswith("transfers")
        assert data["daily_use_readiness"] == "demo_path"
        assert "SQLite remains the single source of truth" in data["guidance"][0]
    finally:
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


def test_local_backup_and_restore_round_trip_replaces_active_sqlite_file(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "operability-roundtrip.db"
    engine, testing_session_local = _build_testing_session_local(db_path)
    try:
        with _api_client(testing_session_local, monkeypatch) as client:
            first_submit = client.post(
                "/api/capture",
                json={"raw_text": "买了咖啡", "source_type": "manual"},
            )
            assert first_submit.status_code == 201
            first_capture_id = first_submit.json()["data"]["capture_id"]

            backup_response = client.post("/api/system/backup", json={})
            assert backup_response.status_code == 200
            backup_data = backup_response.json()["data"]
            backup_path = Path(backup_data["backup_path"])
            assert backup_data["backup_created"] is True
            assert backup_path.exists()

            second_submit = client.post(
                "/api/capture",
                json={"raw_text": "今天花了25元午饭", "source_type": "manual"},
            )
            assert second_submit.status_code == 201
            second_capture_id = second_submit.json()["data"]["capture_id"]

            restore_response = client.post(
                "/api/system/restore",
                json={"source_path": str(backup_path), "create_safety_backup": True},
            )
            assert restore_response.status_code == 200
            restore_data = restore_response.json()["data"]
            assert restore_data["restore_completed"] is True
            assert Path(restore_data["database_path"]) == db_path
            assert restore_data["safety_backup_path"] is not None
            assert Path(restore_data["safety_backup_path"]).exists()

            capture_list_response = client.get("/api/capture")
            assert capture_list_response.status_code == 200
            items = capture_list_response.json()["data"]["items"]
            item_ids = {item["id"] for item in items}
            assert first_capture_id in item_ids
            assert second_capture_id not in item_ids
    finally:
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


def test_capture_bundle_export_and_import_replay_upstream_capture_inputs(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_db_path = tmp_path / "capture-export-source.db"
    source_engine, source_session_local = _build_testing_session_local(source_db_path)

    target_db_path = tmp_path / "capture-import-target.db"
    target_engine, target_session_local = _build_testing_session_local(target_db_path)

    try:
        export_path = tmp_path / "transfer" / "capture-bundle.json"

        with _api_client(source_session_local, monkeypatch) as source_client:
            pending_submit = source_client.post(
                "/api/capture",
                json={"raw_text": "买了咖啡", "source_type": "manual", "source_ref": "wechat"},
            )
            committed_submit = source_client.post(
                "/api/capture",
                json={"raw_text": "今天花了25元午饭", "source_type": "manual", "source_ref": "notes"},
            )
            assert pending_submit.status_code == 201
            assert committed_submit.status_code == 201

            export_response = source_client.post(
                "/api/system/export/capture-bundle",
                json={"destination_path": str(export_path)},
            )
            assert export_response.status_code == 200
            export_data = export_response.json()["data"]
            assert export_data["export_created"] is True
            assert export_data["item_count"] == 2
            assert export_data["skipped_count"] == 0
            assert export_path.exists()

        with _api_client(target_session_local, monkeypatch) as target_client:
            import_response = target_client.post(
                "/api/system/import/capture-bundle",
                json={"source_path": str(export_path)},
            )
            assert import_response.status_code == 200
            import_data = import_response.json()["data"]
            assert import_data["import_completed"] is True
            assert import_data["imported_count"] == 2
            assert import_data["pending_count"] == 1
            assert import_data["committed_count"] == 1

            capture_list_response = target_client.get("/api/capture")
            assert capture_list_response.status_code == 200
            items = capture_list_response.json()["data"]["items"]
            assert len(items) == 2
            summaries = {item["summary"] for item in items}
            assert any("咖啡" in (summary or "") for summary in summaries)
            assert any("25元午饭" in (summary or "") for summary in summaries)
    finally:
        Base.metadata.drop_all(bind=source_engine)
        source_engine.dispose()
        Base.metadata.drop_all(bind=target_engine)
        target_engine.dispose()
