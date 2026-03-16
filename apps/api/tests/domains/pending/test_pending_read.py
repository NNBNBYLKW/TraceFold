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
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingStatus
from app.main import app


@pytest.fixture
def db(tmp_path: Path) -> Generator[Session, None, None]:
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'pending-read.db'}",
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


def test_get_pending_returns_stable_empty_structure(api_client: TestClient) -> None:
    response = api_client.get("/api/pending")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data == {
        "items": [],
        "page": 1,
        "page_size": 20,
        "total": 0,
        "next_pending_item_id": None,
    }


def test_get_pending_defaults_to_open_and_marks_next_to_review(
    api_client: TestClient,
    db: Session,
) -> None:
    oldest_open = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 10, 8, 0, 0),
        reason="oldest open item",
    )
    newest_open = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 11, 8, 0, 0),
        reason="newest open item",
        corrected_payload_json={"metric_type": "sleep", "value_text": "8h"},
    )
    _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        status=PendingStatus.CONFIRMED,
        created_at=datetime(2026, 3, 12, 8, 0, 0),
        reason="resolved item",
        resolved_at=datetime(2026, 3, 12, 9, 0, 0),
    )

    response = api_client.get("/api/pending")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["total"] == 2
    assert data["next_pending_item_id"] == oldest_open.id
    assert [item["id"] for item in data["items"]] == [newest_open.id, oldest_open.id]
    assert data["items"][0]["status"] == PendingStatus.OPEN
    assert data["items"][0]["has_corrected_payload"] is True
    assert data["items"][0]["is_next_to_review"] is False
    assert data["items"][1]["is_next_to_review"] is True


def test_get_pending_filters_by_status_target_domain_and_created_at(
    api_client: TestClient,
    db: Session,
) -> None:
    _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.CONFIRMED,
        created_at=datetime(2026, 3, 1, 9, 0, 0),
        resolved_at=datetime(2026, 3, 15, 9, 0, 0),
        reason="created too early for date window",
    )
    matching_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.CONFIRMED,
        created_at=datetime(2026, 3, 12, 9, 0, 0),
        resolved_at=datetime(2026, 3, 16, 9, 0, 0),
        reason="matching item",
    )
    _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        status=PendingStatus.CONFIRMED,
        created_at=datetime(2026, 3, 12, 10, 0, 0),
        resolved_at=datetime(2026, 3, 16, 10, 0, 0),
        reason="wrong domain",
    )

    response = api_client.get(
        "/api/pending",
        params={
            "status": "confirmed",
            "target_domain": "expense",
            "date_from": "2026-03-10T00:00:00",
            "date_to": "2026-03-13T00:00:00",
        },
    )

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["total"] == 1
    assert [item["id"] for item in data["items"]] == [matching_item.id]


def test_get_pending_supports_pagination_and_sorting(
    api_client: TestClient,
    db: Session,
) -> None:
    oldest = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 10, 8, 0, 0),
        reason="oldest",
    )
    middle = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 11, 8, 0, 0),
        reason="middle",
    )
    _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 12, 8, 0, 0),
        reason="newest",
    )

    response = api_client.get(
        "/api/pending",
        params={
            "sort_by": "created_at",
            "sort_order": "asc",
            "page": 2,
            "page_size": 1,
        },
    )

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["page"] == 2
    assert data["page_size"] == 1
    assert data["total"] == 3
    assert [item["id"] for item in data["items"]] == [middle.id]
    assert data["next_pending_item_id"] == oldest.id


def test_get_pending_detail_returns_payloads_for_resolved_item(
    api_client: TestClient,
    db: Session,
) -> None:
    pending_item = _create_pending_fixture(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        status=PendingStatus.FORCED,
        created_at=datetime(2026, 3, 10, 8, 0, 0),
        resolved_at=datetime(2026, 3, 10, 9, 0, 0),
        reason="manual insert",
        proposed_payload_json={"title": "draft", "content": "body"},
        corrected_payload_json={"title": "final", "content": "final body"},
    )

    response = api_client.get(f"/api/pending/{pending_item.id}")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data == {
        "id": pending_item.id,
        "status": PendingStatus.FORCED,
        "target_domain": ParseTargetDomain.KNOWLEDGE,
        "reason": "manual insert",
        "proposed_payload_json": {"title": "draft", "content": "body"},
        "corrected_payload_json": {"title": "final", "content": "final body"},
        "created_at": "2026-03-10T08:00:00",
        "resolved_at": "2026-03-10T09:00:00",
        "source_capture_id": pending_item.capture_id,
        "parse_result_id": pending_item.parse_result_id,
    }


@pytest.mark.parametrize(
    ("params", "code"),
    [
        ({"sort_by": "invalid"}, "INVALID_SORT_BY"),
        ({"sort_order": "sideways"}, "INVALID_SORT_ORDER"),
        ({"page_size": "101"}, "INVALID_PAGE_SIZE"),
    ],
)
def test_get_pending_rejects_invalid_parameters(
    api_client: TestClient,
    params: dict[str, str],
    code: str,
) -> None:
    response = api_client.get("/api/pending", params=params)

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == code


def _create_pending_fixture(
    db: Session,
    *,
    target_domain: str,
    status: str,
    created_at: datetime,
    reason: str,
    resolved_at: datetime | None = None,
    proposed_payload_json: dict | None = None,
    corrected_payload_json: dict | None = None,
):
    payload = proposed_payload_json or _default_payload(target_domain)
    capture_status = CaptureStatus.PENDING if status == PendingStatus.OPEN else CaptureStatus.COMMITTED

    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=capture_status,
    )
    capture.finalized_at = resolved_at

    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.5,
        confidence_level=ParseConfidenceLevel.LOW,
        parsed_payload_json=payload,
        parser_name="test",
        parser_version="0.1.0",
    )

    pending_item = pending_repository.create_pending_item(
        db,
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=target_domain,
        proposed_payload_json=payload,
        corrected_payload_json=corrected_payload_json,
        reason=reason,
        status=status,
    )
    pending_item.created_at = created_at
    pending_item.resolved_at = resolved_at
    db.commit()
    return pending_item


def _default_payload(target_domain: str) -> dict[str, str]:
    if target_domain == ParseTargetDomain.EXPENSE:
        return {"amount": "18.00", "currency": "USD"}
    if target_domain == ParseTargetDomain.KNOWLEDGE:
        return {"title": "note", "content": "content"}
    if target_domain == ParseTargetDomain.HEALTH:
        return {"metric_type": "sleep", "value_text": "8h"}
    return {"raw_text": "unknown"}
