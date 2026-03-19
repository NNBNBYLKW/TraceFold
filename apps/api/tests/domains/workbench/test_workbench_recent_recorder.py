from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseConfidenceLevel, ParseResult, ParseTargetDomain
from app.domains.pending.models import PendingItem, PendingStatus
from app.domains.workbench.models import WorkbenchRecentContext
from app.domains.workbench import service as workbench_service
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
        f"sqlite:///{tmp_path / 'workbench-recent.db'}",
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


def _create_capture(
    db: Session,
    *,
    raw_text: str,
    status: str,
) -> CaptureRecord:
    capture = CaptureRecord(
        source_type="test",
        source_ref="seed",
        raw_text=raw_text,
        status=status,
    )
    db.add(capture)
    db.flush()
    return capture


def _create_parse_result(
    db: Session,
    *,
    capture: CaptureRecord,
    payload: dict[str, object],
) -> ParseResult:
    parse_result = ParseResult(
        capture_id=capture.id,
        target_domain=ParseTargetDomain.EXPENSE,
        confidence_score=0.5,
        confidence_level=ParseConfidenceLevel.MEDIUM,
        parsed_payload_json=payload,
        parser_name="test",
        parser_version="1",
    )
    db.add(parse_result)
    db.flush()
    return parse_result


def _seed_pending_item(db: Session, *, status: str = PendingStatus.OPEN) -> PendingItem:
    capture_status = CaptureStatus.PENDING if status == PendingStatus.OPEN else CaptureStatus.COMMITTED
    capture = _create_capture(db, raw_text="午饭 30", status=capture_status)
    parse_result = _create_parse_result(
        db,
        capture=capture,
        payload={"amount": "30", "currency": "CNY", "category": "food"},
    )
    pending_item = PendingItem(
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=ParseTargetDomain.EXPENSE,
        status=status,
        proposed_payload_json={"amount": "30", "currency": "CNY", "category": "food"},
        corrected_payload_json=None,
        reason="needs review",
    )
    db.add(pending_item)
    db.commit()
    db.refresh(pending_item)
    return pending_item


def test_recent_view_is_recorded_only_after_successful_read(api_client: TestClient, db: Session) -> None:
    pending_item = _seed_pending_item(db)

    success_response = api_client.get(f"/api/pending/{pending_item.id}")
    failure_response = api_client.get("/api/pending/99999")

    assert success_response.status_code == 200
    assert failure_response.status_code == 404
    assert db.query(WorkbenchRecentContext).count() == 1

    recent = db.query(WorkbenchRecentContext).first()
    assert recent is not None
    assert recent.object_type == "pending"
    assert recent.action_type == "viewed"


def test_recent_action_is_recorded_only_after_successful_action(api_client: TestClient, db: Session) -> None:
    pending_item = _seed_pending_item(db)

    confirm_response = api_client.post(
        f"/api/pending/{pending_item.id}/confirm",
        json={"note": "ok"},
    )
    repeat_response = api_client.post(
        f"/api/pending/{pending_item.id}/confirm",
        json={"note": "again"},
    )

    assert confirm_response.status_code == 200
    assert repeat_response.status_code == 409

    acted_entries = (
        db.query(WorkbenchRecentContext)
        .filter(
            WorkbenchRecentContext.object_type == "pending",
            WorkbenchRecentContext.object_id == str(pending_item.id),
            WorkbenchRecentContext.action_type == "acted",
        )
        .all()
    )
    assert len(acted_entries) == 1
    assert acted_entries[0].context_payload_json["status"] == "confirmed"


def test_recent_recorder_failure_does_not_break_main_action(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    pending_item = _seed_pending_item(db)

    def explode(*args, **kwargs):
        raise RuntimeError("recent recorder failure")

    monkeypatch.setattr(
        "app.domains.workbench.service.repository.get_recent_context_by_object_action",
        explode,
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/confirm",
        json={"note": "still confirm"},
    )

    assert response.status_code == 200
    assert response.json()["data"]["status"] == "confirmed"
    assert db.query(WorkbenchRecentContext).count() == 0


def test_recent_duplicate_replacement_keeps_single_latest_entry(db: Session) -> None:
    workbench_service.record_recent_context_best_effort(
        db,
        object_type="expense",
        object_id="7",
        action_type="viewed",
        title_snapshot="Expense old",
        route_snapshot="/expense/7",
        context_payload_json={"version": "old"},
    )
    workbench_service.record_recent_context_best_effort(
        db,
        object_type="expense",
        object_id="7",
        action_type="viewed",
        title_snapshot="Expense new",
        route_snapshot="/expense/7",
        context_payload_json={"version": "new"},
    )

    rows = db.query(WorkbenchRecentContext).all()
    assert len(rows) == 1
    assert rows[0].title_snapshot == "Expense new"
    assert rows[0].context_payload_json["version"] == "new"
