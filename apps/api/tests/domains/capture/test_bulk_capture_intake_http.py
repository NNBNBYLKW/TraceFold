from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.main import app


@pytest.fixture
def db(tmp_path: Path) -> Generator[Session, None, None]:
    import app.domains.alerts.models  # noqa: F401
    import app.domains.ai_derivations.models  # noqa: F401
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'capture-bulk-http.db'}",
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
    session = testing_session_local()
    try:
        yield session
    finally:
        session.close()
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


@pytest.fixture
def api_client(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        yield db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_bulk_capture_preview_splits_text_file_by_blank_lines(api_client: TestClient) -> None:
    response = api_client.post(
        "/api/capture/bulk-intake/preview",
        json={
            "file_name": "daily-notes.txt",
            "text_content": "买了咖啡\n\n今天花了25元午饭\n\n   \n\n补一句恢复情况",
        },
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["success"] is True
    assert payload["message"] == "Bulk capture preview generated."

    data = payload["data"]
    assert data["file_name"] == "daily-notes.txt"
    assert data["split_strategy"] == "split_by_blank_lines"
    assert data["candidate_count"] == 3
    assert data["valid_count"] == 3
    assert data["invalid_count"] == 0
    assert [candidate["preview"] for candidate in data["candidates"]] == [
        "买了咖啡",
        "今天花了25元午饭",
        "补一句恢复情况",
    ]


def test_bulk_capture_import_creates_capture_records_only(api_client: TestClient, db: Session) -> None:
    response = api_client.post(
        "/api/capture/bulk-intake/import",
        json={
            "file_name": "daily-notes.txt",
            "entries": [
                "买了咖啡",
                "今天花了25元午饭",
                "   ",
            ],
        },
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["success"] is True
    assert payload["message"] == "Bulk capture import completed."

    data = payload["data"]
    assert data["file_name"] == "daily-notes.txt"
    assert data["imported_count"] == 2
    assert data["skipped_count"] == 1
    assert data["pending_count"] == 1
    assert data["committed_count"] == 1
    assert len(data["capture_ids"]) == 2

    captures = [capture_repository.get_capture_by_id(db, capture_id) for capture_id in data["capture_ids"]]
    assert all(capture is not None for capture in captures)
    assert {capture.status for capture in captures if capture is not None} == {
        CaptureStatus.PENDING,
        CaptureStatus.COMMITTED,
    }
    assert all(capture.source_type == "file_import" for capture in captures if capture is not None)
    assert all(capture.source_ref is not None and "daily-notes.txt" in capture.source_ref for capture in captures if capture is not None)


def test_bulk_capture_preview_rejects_unsupported_file_type(api_client: TestClient) -> None:
    response = api_client.post(
        "/api/capture/bulk-intake/preview",
        json={
            "file_name": "daily-notes.csv",
            "text_content": "买了咖啡",
        },
    )

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == "UNSUPPORTED_BULK_CAPTURE_FILE_TYPE"
