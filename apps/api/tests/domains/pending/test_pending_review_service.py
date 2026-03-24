from __future__ import annotations

from datetime import datetime
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.exceptions import BadRequestError, ConflictError
from app.db.base import Base
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus, ParseConfidenceLevel, ParseTargetDomain
from app.domains.expense.models import ExpenseRecord
from app.domains.health.models import HealthRecord
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingItem, PendingReviewAction, PendingStatus
from app.domains.pending.service import (
    confirm_pending_item,
    discard_pending_item,
    fix_pending_item,
    force_insert_pending_item,
)


@pytest.fixture
def db(tmp_path: Path) -> Session:
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'pending-review.db'}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    TestingSessionLocal = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )

    Base.metadata.create_all(bind=engine)
    session = TestingSessionLocal()
    try:
        yield session
    finally:
        session.close()
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


def test_fix_open_pending_updates_corrected_payload_and_keeps_open(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "18.50", "currency": "USD"},
    )

    updated = fix_pending_item(
        db,
        pending_item_id=pending_item.id,
        corrected_payload_json={"amount": "20.00", "currency": "USD", "note": "tip included"},
        note="manual adjustment",
    )

    action = _get_only_action(db)
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)

    assert updated.status == PendingStatus.OPEN
    assert updated.corrected_payload_json == {"amount": "20.00", "currency": "USD", "note": "tip included"}
    assert capture is not None
    assert capture.status == CaptureStatus.PENDING
    assert action.action_type == "fix"
    assert action.before_payload_json == {"amount": "18.50", "currency": "USD"}
    assert action.after_payload_json == {"amount": "20.00", "currency": "USD", "note": "tip included"}


def test_confirm_uses_proposed_payload_when_corrected_is_absent(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "88.00", "currency": "CNY", "category": "meal"},
    )

    updated = confirm_pending_item(db, pending_item_id=pending_item.id, note="looks good")

    record = db.query(ExpenseRecord).one()
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)

    assert updated.status == PendingStatus.CONFIRMED
    assert updated.resolved_at is not None
    assert record.amount == "88.00"
    assert record.currency == "CNY"
    assert record.source_pending_id == pending_item.id
    assert capture is not None
    assert capture.status == CaptureStatus.COMMITTED
    assert capture.finalized_at is not None


def test_confirm_uses_corrected_payload_when_valid(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        proposed_payload={"title": "draft title", "content": ""},
        corrected_payload={"title": "final title", "content": "final content"},
    )

    updated = confirm_pending_item(db, pending_item_id=pending_item.id)

    entry = db.query(KnowledgeEntry).one()

    assert updated.status == PendingStatus.CONFIRMED
    assert entry.title == "final title"
    assert entry.content == "final content"
    assert entry.source_pending_id == pending_item.id


def test_confirm_rejects_invalid_corrected_payload_without_falling_back(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "50", "currency": "USD"},
        corrected_payload={"amount": "", "currency": "USD"},
    )

    with pytest.raises(BadRequestError) as exc_info:
        confirm_pending_item(db, pending_item_id=pending_item.id)

    db.expire_all()
    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)

    assert exc_info.value.code == "INVALID_CORRECTED_PAYLOAD"
    assert refreshed is not None
    assert refreshed.status == PendingStatus.OPEN
    assert refreshed.resolved_at is None
    assert capture is not None
    assert capture.status == CaptureStatus.PENDING
    assert capture.finalized_at is None
    assert db.query(ExpenseRecord).count() == 0
    assert db.query(PendingReviewAction).count() == 0


def test_discard_marks_pending_and_capture_as_discarded(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.UNKNOWN,
        proposed_payload={"raw_text": "unclear input"},
    )

    updated = discard_pending_item(db, pending_item_id=pending_item.id, note="not useful")

    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)
    action = _get_only_action(db)

    assert updated.status == PendingStatus.DISCARDED
    assert updated.resolved_at is not None
    assert capture is not None
    assert capture.status == CaptureStatus.DISCARDED
    assert capture.finalized_at is not None
    assert action.action_type == "discard"


def test_force_insert_writes_fact_and_marks_pending_forced(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        proposed_payload={"metric_type": "weight", "value_text": "70kg"},
    )

    updated = force_insert_pending_item(db, pending_item_id=pending_item.id, note="force through")

    record = db.query(HealthRecord).one()
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)

    assert updated.status == PendingStatus.FORCED
    assert updated.resolved_at is not None
    assert record.metric_type == "weight"
    assert record.value_text == "70kg"
    assert record.source_pending_id == pending_item.id
    assert capture is not None
    assert capture.status == CaptureStatus.COMMITTED


def test_resolved_pending_cannot_be_operated_again(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "12", "currency": "USD"},
        pending_status=PendingStatus.CONFIRMED,
        pending_resolved_at=datetime.utcnow(),
        capture_status=CaptureStatus.COMMITTED,
        capture_finalized_at=datetime.utcnow(),
    )
    logged_events: list[dict[str, object]] = []

    def capture_log_event(logger, *, level, event, **fields) -> None:
        logged_events.append(
            {
                "level": level,
                "event": event,
                **fields,
            }
        )

    monkeypatch.setattr("app.domains.pending.service.log_event", capture_log_event)

    with pytest.raises(ConflictError) as exc_info:
        fix_pending_item(
            db,
            pending_item_id=pending_item.id,
            corrected_payload_json={"amount": "14", "currency": "USD"},
        )

    assert exc_info.value.code == "PENDING_ALREADY_RESOLVED"
    assert db.query(PendingReviewAction).count() == 0
    assert logged_events == [
        {
            "level": 30,
            "event": "pending_illegal_state_transition_attempt",
            "domain": "pending",
            "pending_item_id": pending_item.id,
            "current_status": PendingStatus.CONFIRMED,
            "attempted_action": "review",
        }
    ]


def test_confirm_invalid_proposed_payload_rolls_back_without_dirty_write(db: Session) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        proposed_payload={"value_text": "70kg"},
    )

    with pytest.raises(BadRequestError) as exc_info:
        confirm_pending_item(db, pending_item_id=pending_item.id)

    db.expire_all()
    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)

    assert exc_info.value.code == "INVALID_PENDING_PAYLOAD"
    assert refreshed is not None
    assert refreshed.status == PendingStatus.OPEN
    assert refreshed.resolved_at is None
    assert capture is not None
    assert capture.status == CaptureStatus.PENDING
    assert capture.finalized_at is None
    assert db.query(HealthRecord).count() == 0
    assert db.query(PendingReviewAction).count() == 0


def _create_pending_fixture(
    db: Session,
    *,
    target_domain: str,
    proposed_payload: dict,
    corrected_payload: dict | None = None,
    pending_status: str = PendingStatus.OPEN,
    pending_resolved_at: datetime | None = None,
    capture_status: str = CaptureStatus.PENDING,
    capture_finalized_at: datetime | None = None,
) -> PendingItem:
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=capture_status,
    )
    capture.finalized_at = capture_finalized_at

    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.5,
        confidence_level=ParseConfidenceLevel.LOW,
        parsed_payload_json=proposed_payload,
        parser_name="test",
        parser_version="0.1.0",
    )

    pending_item = pending_repository.create_pending_item(
        db,
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=target_domain,
        proposed_payload_json=proposed_payload,
        corrected_payload_json=corrected_payload,
        reason="fixture",
        status=pending_status,
    )
    pending_item.resolved_at = pending_resolved_at
    db.commit()
    return pending_item


def _get_only_action(db: Session) -> PendingReviewAction:
    return db.query(PendingReviewAction).one()
