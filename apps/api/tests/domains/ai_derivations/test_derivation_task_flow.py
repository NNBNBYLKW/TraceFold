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
def testing_session_local(tmp_path: Path):
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
        f"sqlite:///{tmp_path / 'ai-derivation-task.db'}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    testing_session = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )
    Base.metadata.create_all(bind=engine)
    try:
        yield testing_session
    finally:
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


@pytest.fixture
def db(testing_session_local) -> Generator[Session, None, None]:
    session = testing_session_local()
    try:
        yield session
    finally:
        session.close()


@pytest.fixture
def api_client(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        yield db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_recompute_endpoint_submits_task_and_refreshes_knowledge_summary(api_client: TestClient, db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Task note",
            "content": "Shared task runtime should carry the formal knowledge_summary recompute path.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    response = api_client.post(f"/api/ai-derivations/knowledge/{entry.id}/recompute")

    assert response.status_code == 201
    submitted = response.json()["data"]
    assert submitted["task_type"] == "knowledge_summary_recompute"
    assert submitted["derivation_kind"] == "knowledge_summary"

    task_detail = api_client.get(f"/api/tasks/{submitted['task_id']}").json()["data"]
    derivation_detail = api_client.get(f"/api/ai-derivations/knowledge/{entry.id}").json()["data"]

    assert task_detail["status"] == "succeeded"
    assert derivation_detail["status"] == "ready"
    assert derivation_detail["model_key"] == "qwen3.5:9b"
    assert derivation_detail["model_version"] == "openai_compatible:knowledge_summary_json_v3"


def test_recompute_task_failure_is_visible_in_task_and_derivation(api_client: TestClient, db: Session, monkeypatch: pytest.MonkeyPatch) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Failure task note",
            "content": "A failed recompute should be visible on both task_runs and ai_derivations.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    monkeypatch.setattr(
        "app.domains.knowledge.ai_summary.build_knowledge_summary_content",
        lambda knowledge_entry: (_ for _ in ()).throw(RuntimeError("knowledge task failure")),
    )

    response = api_client.post(f"/api/ai-derivations/knowledge/{entry.id}/recompute")

    assert response.status_code == 201
    submitted = response.json()["data"]

    task_detail = api_client.get(f"/api/tasks/{submitted['task_id']}").json()["data"]
    derivation_detail = api_client.get(f"/api/ai-derivations/knowledge/{entry.id}").json()["data"]

    assert task_detail["status"] == "failed"
    assert task_detail["error_message"] == "Knowledge summary derivation failed."
    assert derivation_detail["status"] == "failed"
    assert derivation_detail["error_message"] == "knowledge task failure"


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
