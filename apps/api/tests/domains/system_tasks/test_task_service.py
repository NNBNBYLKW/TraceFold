from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.domains.ai_derivations import service as ai_derivations_service
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.domains.knowledge.service import create_knowledge_entry
from app.domains.system_tasks import repository, service
from app.domains.system_tasks.models import TaskTriggerSource
from app.tasks.registry import TASK_TYPE_DASHBOARD_SUMMARY_REFRESH, TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE


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
        f"sqlite:///{tmp_path / 'task-service.db'}",
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


def test_request_and_execute_dashboard_refresh_task_flow(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)

    task_read = service.request_task_run(
        db,
        task_type=TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
        payload_json={"requested_by": "test"},
    )

    assert task_read.status == "pending"
    assert task_read.attempt_count == 0

    service.execute_task(task_id=task_read.id)

    verification_db = testing_session_local()
    try:
        task = repository.get_task_by_id(verification_db, task_read.id)
        assert task is not None
        assert task.status == "succeeded"
        assert task.attempt_count == 1
        assert task.result_json is not None
        assert task.result_json["recent_activity_count"] >= 0
    finally:
        verification_db.close()


def test_request_task_run_allows_manual_trigger_source(
    db: Session,
) -> None:
    task_read = service.request_task_run(
        db,
        task_type=TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
        trigger_source=TaskTriggerSource.MANUAL.value,
    )

    assert task_read.trigger_source == "manual"


def test_cancelled_task_is_not_executed(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)

    task_read = service.request_task_run(
        db,
        task_type=TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
    )
    cancelled = service.cancel_task_run(db, task_read.id)

    service.execute_task(task_id=task_read.id)

    verification_db = testing_session_local()
    try:
        task = repository.get_task_by_id(verification_db, task_read.id)
        assert task is not None
        assert cancelled.status == "cancelled"
        assert task.status == "cancelled"
        assert task.attempt_count == 0
        assert task.result_json is None
    finally:
        verification_db.close()


def test_failed_task_persists_failure_state(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)
    monkeypatch.setattr(
        "app.domains.system_tasks.service.get_task_executor",
        lambda task_type: (lambda db, payload_json, task_id=None: (_ for _ in ()).throw(RuntimeError("task boom"))),
    )

    task_read = service.request_task_run(
        db,
        task_type=TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
    )

    service.execute_task(task_id=task_read.id)

    verification_db = testing_session_local()
    try:
        task = repository.get_task_by_id(verification_db, task_read.id)
        assert task is not None
        assert task.status == "failed"
        assert task.attempt_count == 1
        assert task.error_message == "task boom"
    finally:
        verification_db.close()


def test_request_and_execute_knowledge_summary_recompute_task_flow(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)

    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Task runtime note",
            "content": "A focused derivation task should reuse the shared runtime baseline.",
            "source_text": "Seed content.",
        },
    )
    db.commit()

    request_payload = ai_derivations_service.request_knowledge_summary_recompute(
        db,
        knowledge_id=entry.id,
    )
    task_read = service.request_task_run(
        db,
        task_type=TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE,
        payload_json=request_payload,
    )

    service.execute_task(task_id=task_read.id)

    verification_db = testing_session_local()
    try:
        task = repository.get_task_by_id(verification_db, task_read.id)
        derivation = ai_derivations_service.get_ai_derivation_read(
            verification_db,
            target_type="knowledge",
            target_id=entry.id,
        )
        assert task is not None
        assert task.status == "succeeded"
        assert derivation.status == "ready"
        assert derivation.derivation_kind == "knowledge_summary"
    finally:
        verification_db.close()


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
