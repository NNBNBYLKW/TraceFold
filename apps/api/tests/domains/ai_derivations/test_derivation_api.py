from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.domains.knowledge.service import create_knowledge_entry
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
    import app.domains.workbench.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'ai-derivation-api.db'}",
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


def test_get_ai_derivation_detail_returns_formal_derivation_payload(api_client: TestClient, db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Detail note",
            "content": "Detail endpoint should expose the formal derivation shape.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    response = api_client.get(f"/api/ai-derivations/knowledge/{entry.id}")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["target_type"] == "knowledge"
    assert data["target_id"] == entry.id
    assert data["derivation_kind"] == "knowledge_summary"
    assert data["status"] == "ready"
    assert data["source_basis_json"]["content_fingerprint"]
    assert data["target_domain"] == "knowledge"
    assert data["derivation_type"] == "knowledge_summary"


def test_list_ai_derivations_supports_formal_filters(api_client: TestClient, db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "List note",
            "content": "List endpoint should allow minimal formal filtering.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    response = api_client.get(
        "/api/ai-derivations",
        params={"target_type": "knowledge", "derivation_kind": "knowledge_summary", "status": "ready", "limit": 10},
    )

    assert response.status_code == 200
    body = response.json()["data"]
    assert body["items"]
    assert any(item["target_id"] == entry.id for item in body["items"])


def test_invalidate_ai_derivation_endpoint_marks_row_invalidated(api_client: TestClient, db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Invalidate endpoint note",
            "content": "Invalidate endpoint should expose formal invalidation.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    response = api_client.post(f"/api/ai-derivations/knowledge/{entry.id}/invalidate")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["status"] == "invalidated"
    assert data["invalidated_at"] is not None


def test_recompute_endpoint_rejects_unsupported_target_type(api_client: TestClient) -> None:
    response = api_client.post("/api/ai-derivations/health/1/recompute")

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "INVALID_DERIVATION_TARGET_TYPE"


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
