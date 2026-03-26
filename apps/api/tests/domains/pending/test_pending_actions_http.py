from __future__ import annotations

from collections.abc import Generator
from datetime import datetime
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus, ParseConfidenceLevel, ParseTargetDomain
from app.domains.expense.models import ExpenseRecord
from app.domains.health.models import HealthRecord
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingReviewAction, PendingStatus
from app.main import app, create_app


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
        f"sqlite:///{tmp_path / 'pending-actions-http.db'}",
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


def test_post_pending_confirm_confirms_item_and_writes_formal_record(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "88.00", "currency": "CNY", "category": "meal"},
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/confirm",
        json={"note": "looks good"},
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["success"] is True
    assert payload["message"] == "Pending item confirmed."
    assert payload["data"] == {
        "action_executed": True,
        "action_type": "confirm",
        "pending_id": pending_item.id,
        "status": PendingStatus.CONFIRMED,
        "target_domain": ParseTargetDomain.EXPENSE,
        "source_capture_id": pending_item.capture_id,
    }

    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    expense_record = db.query(ExpenseRecord).one()

    assert refreshed is not None
    assert refreshed.status == PendingStatus.CONFIRMED
    assert expense_record.source_pending_id == pending_item.id


def test_post_pending_discard_discards_item(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.UNKNOWN,
        proposed_payload={"raw_text": "unclear input"},
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/discard",
        json={"note": "not useful"},
    )

    assert response.status_code == 200
    assert response.json()["data"] == {
        "action_executed": True,
        "action_type": "discard",
        "pending_id": pending_item.id,
        "status": PendingStatus.DISCARDED,
        "target_domain": ParseTargetDomain.UNKNOWN,
        "source_capture_id": pending_item.capture_id,
    }

    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    assert refreshed is not None
    assert refreshed.status == PendingStatus.DISCARDED


def test_post_pending_fix_updates_pending_with_minimal_text_correction(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"raw_text": "买了咖啡"},
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/fix",
        json={"correction_text": "今天花了25元午饭"},
    )

    assert response.status_code == 200
    assert response.json()["data"] == {
        "action_executed": True,
        "action_type": "fix",
        "pending_id": pending_item.id,
        "status": PendingStatus.OPEN,
        "target_domain": ParseTargetDomain.EXPENSE,
        "source_capture_id": pending_item.capture_id,
    }

    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    action = db.query(PendingReviewAction).one()

    assert refreshed is not None
    assert refreshed.status == PendingStatus.OPEN
    assert refreshed.corrected_payload_json == {
        "amount": "25",
        "currency": "CNY",
        "category": "meal",
        "note": "今天花了25元午饭",
    }
    assert action.action_type == "fix"
    assert action.note == "今天花了25元午饭"


def test_post_pending_force_insert_resolves_item_through_force_path(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        proposed_payload={"metric_type": "sleep", "value_text": "8h"},
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/force_insert",
        json={"note": "force through"},
    )

    assert response.status_code == 200
    assert response.json()["data"] == {
        "action_executed": True,
        "action_type": "force_insert",
        "pending_id": pending_item.id,
        "status": PendingStatus.FORCED,
        "target_domain": ParseTargetDomain.HEALTH,
        "source_capture_id": pending_item.capture_id,
    }

    refreshed = pending_repository.get_pending_item_by_id(db, pending_item.id)
    health_record = db.query(HealthRecord).one()

    assert refreshed is not None
    assert refreshed.status == PendingStatus.FORCED
    assert health_record.source_pending_id == pending_item.id


def test_post_pending_confirm_rejects_already_resolved_item(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "12", "currency": "USD"},
        pending_status=PendingStatus.CONFIRMED,
        pending_resolved_at=datetime(2026, 3, 16, 9, 0, 0),
        capture_status=CaptureStatus.COMMITTED,
        capture_finalized_at=datetime(2026, 3, 16, 9, 0, 0),
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/confirm",
        json={"note": "retry"},
    )

    assert response.status_code == 409
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == "PENDING_ALREADY_RESOLVED"


def test_post_pending_confirm_returns_not_found_for_missing_item(api_client: TestClient) -> None:
    response = api_client.post(
        "/api/pending/999999/confirm",
        json={"note": "missing"},
    )

    assert response.status_code == 404
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == "PENDING_ITEM_NOT_FOUND"


def test_post_pending_fix_rejects_blank_correction_text(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"raw_text": "买了咖啡"},
    )

    response = api_client.post(
        f"/api/pending/{pending_item.id}/fix",
        json={"correction_text": "   "},
    )

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == "INVALID_FIX_INPUT"


def _create_pending_fixture(
    db: Session,
    *,
    target_domain: str,
    proposed_payload: dict,
    pending_status: str = PendingStatus.OPEN,
    pending_resolved_at: datetime | None = None,
    capture_status: str = CaptureStatus.PENDING,
    capture_finalized_at: datetime | None = None,
):
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
        reason="fixture",
        status=pending_status,
    )
    pending_item.resolved_at = pending_resolved_at
    db.commit()
    return pending_item

def test_pending_action_internal_failure_uses_uniform_error_envelope(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        proposed_payload={"amount": "88.00", "currency": "CNY", "category": "meal"},
    )

    def raise_unexpected(*args, **kwargs):
        raise RuntimeError("sensitive internal detail")

    monkeypatch.setattr(
        "app.domains.pending.service.apply_pending_confirm_action",
        raise_unexpected,
    )

    def override_get_db() -> Generator[Session, None, None]:
        yield db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    monkeypatch.setattr("app.main.settings.debug", False)
    local_app = create_app()
    local_app.dependency_overrides[get_db] = override_get_db
    with TestClient(local_app, raise_server_exceptions=False) as test_client:
        response = test_client.post(
            f"/api/pending/{pending_item.id}/confirm",
            json={"note": "boom"},
        )
    local_app.dependency_overrides.clear()

    assert response.status_code == 500
    assert response.json() == {
        "success": False,
        "message": "Internal server error.",
        "data": None,
        "meta": None,
        "error": {
            "code": "INTERNAL_SERVER_ERROR",
            "details": None,
        },
    }



