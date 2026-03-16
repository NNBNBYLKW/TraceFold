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
from app.domains.dashboard import service as dashboard_service
from app.domains.expense.service import create_expense_record
from app.domains.health.service import create_health_record
from app.domains.knowledge.service import create_knowledge_entry
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingStatus
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
        f"sqlite:///{tmp_path / 'dashboard.db'}",
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


def test_dashboard_returns_stable_empty_structure(
    api_client: TestClient,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    fixed_now = datetime(2026, 3, 16, 12, 0, 0)
    monkeypatch.setattr(dashboard_service, "_utcnow", lambda: fixed_now)

    response = api_client.get("/api/dashboard")

    assert response.status_code == 200
    payload = response.json()
    assert payload["message"] == "Dashboard fetched."

    data = payload["data"]
    assert set(data.keys()) == {
        "pending_summary",
        "alert_summary",
        "quick_links",
        "expense_summary",
        "knowledge_summary",
        "health_summary",
        "recent_activity",
    }
    assert data["pending_summary"] == {
        "open_count": 0,
        "open_count_by_target_domain": {},
        "opened_in_last_7_days": 0,
        "resolved_in_last_7_days": 0,
        "href": "/pending",
    }
    assert data["alert_summary"] == {
        "open_count": 0,
        "recent_open_items": [],
        "href": "/health?focus=alerts",
    }
    assert data["quick_links"] == [
        {"label": "View pending items", "href": "/pending"},
        {"label": "View expenses", "href": "/expense"},
        {"label": "View knowledge", "href": "/knowledge"},
        {"label": "View health", "href": "/health"},
    ]
    assert data["expense_summary"] == {
        "created_in_current_month": 0,
        "amount_by_currency_current_month": {},
        "latest_expense_created_at": None,
        "href": "/expense",
    }
    assert data["knowledge_summary"] == {
        "created_in_last_7_days": 0,
        "created_in_last_30_days": 0,
        "latest_knowledge_created_at": None,
        "href": "/knowledge",
    }
    assert set(data["health_summary"].keys()) == {
        "created_in_last_7_days",
        "latest_health_created_at",
        "recent_metric_types",
        "href",
    }
    assert data["health_summary"] == {
        "created_in_last_7_days": 0,
        "latest_health_created_at": None,
        "recent_metric_types": [],
        "href": "/health",
    }
    assert data["recent_activity"] == []


def test_dashboard_aggregates_summaries_with_pending_windows_and_currency_groups(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    fixed_now = datetime(2026, 3, 16, 12, 0, 0)
    monkeypatch.setattr(dashboard_service, "_utcnow", lambda: fixed_now)

    _create_expense_fact(
        db,
        created_at=datetime(2026, 3, 2, 9, 0, 0),
        amount="10.50",
        currency="USD",
        category="food",
        note="breakfast",
        with_pending=False,
    )
    _create_expense_fact(
        db,
        created_at=datetime(2026, 3, 10, 18, 0, 0),
        amount="5.00",
        currency="USD",
        category="transport",
        note="bus",
        with_pending=False,
    )
    _create_expense_fact(
        db,
        created_at=datetime(2026, 3, 12, 20, 0, 0),
        amount="20.00",
        currency="EUR",
        category="meal",
        note="dinner",
        with_pending=False,
    )
    _create_expense_fact(
        db,
        created_at=datetime(2026, 2, 28, 20, 0, 0),
        amount="99.00",
        currency="USD",
        category="old",
        note="previous month",
        with_pending=False,
    )

    _create_knowledge_fact(
        db,
        created_at=datetime(2026, 3, 15, 8, 0, 0),
        title="Recent note",
        content="recent knowledge",
        source_text=None,
        with_pending=False,
    )
    _create_knowledge_fact(
        db,
        created_at=datetime(2026, 3, 1, 8, 0, 0),
        title="Monthly note",
        content="month window",
        source_text=None,
        with_pending=False,
    )
    _create_knowledge_fact(
        db,
        created_at=datetime(2026, 2, 10, 8, 0, 0),
        title="Old note",
        content="outside thirty days",
        source_text=None,
        with_pending=False,
    )

    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 16, 11, 30, 0),
        metric_type="weight",
        value_text="70kg",
        note=None,
        with_pending=False,
    )
    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 14, 11, 30, 0),
        metric_type="sleep",
        value_text="8h",
        note=None,
        with_pending=False,
    )
    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 12, 11, 30, 0),
        metric_type="steps",
        value_text="9000",
        note=None,
        with_pending=False,
    )
    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 11, 11, 30, 0),
        metric_type="heart_rate",
        value_text="62",
        note=None,
        with_pending=False,
    )
    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 10, 11, 30, 0),
        metric_type="blood_pressure",
        value_text="120/79",
        note=None,
        with_pending=False,
    )
    _create_health_fact(
        db,
        created_at=datetime(2026, 3, 9, 13, 0, 0),
        metric_type="temperature",
        value_text="36.5",
        note=None,
        with_pending=False,
    )

    _create_pending_item(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 15, 10, 0, 0),
    )
    _create_pending_item(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 14, 10, 0, 0),
    )
    _create_pending_item(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 8, 10, 0, 0),
    )
    _create_pending_item(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.CONFIRMED,
        created_at=datetime(2026, 3, 13, 10, 0, 0),
        resolved_at=datetime(2026, 3, 15, 9, 0, 0),
    )
    _create_pending_item(
        db,
        target_domain=ParseTargetDomain.UNKNOWN,
        status=PendingStatus.DISCARDED,
        created_at=datetime(2026, 3, 1, 10, 0, 0),
        resolved_at=datetime(2026, 3, 7, 9, 0, 0),
    )

    response = api_client.get("/api/dashboard")

    assert response.status_code == 200
    data = response.json()["data"]
    assert data["pending_summary"] == {
        "open_count": 3,
        "open_count_by_target_domain": {
            "expense": 1,
            "health": 1,
            "knowledge": 1,
        },
        "opened_in_last_7_days": 2,
        "resolved_in_last_7_days": 1,
        "href": "/pending",
    }
    assert data["expense_summary"] == {
        "created_in_current_month": 3,
        "amount_by_currency_current_month": {
            "EUR": "20.00",
            "USD": "15.50",
        },
        "latest_expense_created_at": "2026-03-12T20:00:00",
        "href": "/expense",
    }
    assert data["knowledge_summary"] == {
        "created_in_last_7_days": 1,
        "created_in_last_30_days": 2,
        "latest_knowledge_created_at": "2026-03-15T08:00:00",
        "href": "/knowledge",
    }
    assert set(data["health_summary"].keys()) == {
        "created_in_last_7_days",
        "latest_health_created_at",
        "recent_metric_types",
        "href",
    }
    assert data["health_summary"] == {
        "created_in_last_7_days": 6,
        "latest_health_created_at": "2026-03-16T11:30:00",
        "recent_metric_types": [
            "weight",
            "sleep",
            "steps",
            "heart_rate",
            "blood_pressure",
        ],
        "href": "/health",
    }
    assert data["alert_summary"] == {
        "open_count": 0,
        "recent_open_items": [],
        "href": "/health?focus=alerts",
    }


def test_dashboard_recent_activity_uses_only_allowed_sources_and_is_sorted(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    fixed_now = datetime(2026, 3, 16, 12, 0, 0)
    monkeypatch.setattr(dashboard_service, "_utcnow", lambda: fixed_now)

    expense = _create_expense_fact(
        db,
        created_at=datetime(2026, 3, 16, 11, 0, 0),
        amount="28.00",
        currency="USD",
        category="food",
        note="lunch",
        with_pending=False,
    )
    knowledge = _create_knowledge_fact(
        db,
        created_at=datetime(2026, 3, 16, 10, 0, 0),
        title="Activity note",
        content="recent knowledge entry",
        source_text=None,
        with_pending=False,
    )
    health = _create_health_fact(
        db,
        created_at=datetime(2026, 3, 16, 9, 0, 0),
        metric_type="sleep",
        value_text="7h",
        note=None,
        with_pending=False,
    )
    pending_fix = _create_pending_item(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 16, 8, 0, 0),
    )
    pending_discard = _create_pending_item(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        status=PendingStatus.DISCARDED,
        created_at=datetime(2026, 3, 16, 7, 0, 0),
        resolved_at=datetime(2026, 3, 16, 7, 30, 0),
    )
    ignored_pending = _create_pending_item(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        status=PendingStatus.OPEN,
        created_at=datetime(2026, 3, 16, 6, 0, 0),
    )
    _create_pending_review_action(
        db,
        pending_item_id=pending_fix.id,
        action_type="fix",
        note="manual correction",
        created_at=datetime(2026, 3, 16, 11, 30, 0),
    )
    _create_pending_review_action(
        db,
        pending_item_id=pending_discard.id,
        action_type="discard",
        note="not needed",
        created_at=datetime(2026, 3, 16, 8, 30, 0),
    )

    response = api_client.get("/api/dashboard")

    assert response.status_code == 200
    recent_activity = response.json()["data"]["recent_activity"]
    assert [item["occurred_at"] for item in recent_activity] == [
        "2026-03-16T11:30:00",
        "2026-03-16T11:00:00",
        "2026-03-16T10:00:00",
        "2026-03-16T09:00:00",
        "2026-03-16T08:30:00",
    ]
    assert {item["activity_type"] for item in recent_activity} == {
        "formal_record_created",
        "pending_review_action",
    }
    assert recent_activity == [
        {
            "activity_type": "pending_review_action",
            "occurred_at": "2026-03-16T11:30:00",
            "target_domain": "expense",
            "target_id": pending_fix.id,
            "title_or_preview": "manual correction",
            "action_label": "Updated pending item",
            "href": "/pending",
        },
        {
            "activity_type": "formal_record_created",
            "occurred_at": "2026-03-16T11:00:00",
            "target_domain": "expense",
            "target_id": expense.id,
            "title_or_preview": "28.00 USD",
            "action_label": "Created expense record",
            "href": f"/expense/{expense.id}",
        },
        {
            "activity_type": "formal_record_created",
            "occurred_at": "2026-03-16T10:00:00",
            "target_domain": "knowledge",
            "target_id": knowledge.id,
            "title_or_preview": "Activity note",
            "action_label": "Created knowledge entry",
            "href": f"/knowledge/{knowledge.id}",
        },
        {
            "activity_type": "formal_record_created",
            "occurred_at": "2026-03-16T09:00:00",
            "target_domain": "health",
            "target_id": health.id,
            "title_or_preview": "sleep: 7h",
            "action_label": "Created health record",
            "href": f"/health/{health.id}",
        },
        {
            "activity_type": "pending_review_action",
            "occurred_at": "2026-03-16T08:30:00",
            "target_domain": "knowledge",
            "target_id": pending_discard.id,
            "title_or_preview": "not needed",
            "action_label": "Discarded pending item",
            "href": "/pending",
        },
    ]
    assert all(item["target_id"] != ignored_pending.id for item in recent_activity)


def test_dashboard_alert_summary_uses_only_open_alerts_and_sorts_by_severity_then_triggered_at(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    from app.domains.alerts.service import upsert_alert_result

    fixed_now = datetime(2026, 3, 16, 12, 0, 0)
    monkeypatch.setattr(dashboard_service, "_utcnow", lambda: fixed_now)

    high_record = _create_health_fact(
        db,
        created_at=datetime(2026, 3, 15, 10, 0, 0),
        metric_type="heart_rate",
        value_text="135",
        note=None,
        with_pending=False,
    )
    warning_record = _create_health_fact(
        db,
        created_at=datetime(2026, 3, 15, 11, 0, 0),
        metric_type="sleep_duration",
        value_text="320",
        note=None,
        with_pending=False,
    )
    info_record = _create_health_fact(
        db,
        created_at=datetime(2026, 3, 15, 12, 0, 0),
        metric_type="blood_pressure",
        value_text="135/82",
        note=None,
        with_pending=False,
    )
    viewed_record = _create_health_fact(
        db,
        created_at=datetime(2026, 3, 15, 13, 0, 0),
        metric_type="heart_rate",
        value_text="132",
        note=None,
        with_pending=False,
    )

    upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=high_record.id,
        rule_code="HEALTH_HEART_RATE_HIGH_V1",
        severity="high",
        status="open",
        title="High alert",
        message="Should stay first even if not the newest alert.",
        explanation=None,
        triggered_at=datetime(2026, 3, 16, 9, 0, 0),
    )
    upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=warning_record.id,
        rule_code="HEALTH_SLEEP_DURATION_WARNING_V1",
        severity="warning",
        status="open",
        title="Warning alert",
        message="Should stay ahead of info.",
        explanation=None,
        triggered_at=datetime(2026, 3, 16, 10, 0, 0),
    )
    upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=info_record.id,
        rule_code="HEALTH_BLOOD_PRESSURE_INFO_V1",
        severity="info",
        status="open",
        title="Info alert",
        message="Newest but lower priority.",
        explanation=None,
        triggered_at=datetime(2026, 3, 16, 11, 59, 0),
    )
    upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=viewed_record.id,
        rule_code="HEALTH_HEART_RATE_HIGH_V1",
        severity="high",
        status="viewed",
        title="Viewed high alert",
        message="Should not appear on dashboard.",
        explanation=None,
        triggered_at=datetime(2026, 3, 16, 11, 58, 0),
        viewed_at=datetime(2026, 3, 16, 11, 58, 30),
    )
    db.commit()

    response = api_client.get("/api/dashboard")

    assert response.status_code == 200
    alert_summary = response.json()["data"]["alert_summary"]
    assert alert_summary["open_count"] == 3
    assert [item["source_record_id"] for item in alert_summary["recent_open_items"]] == [
        high_record.id,
        warning_record.id,
        info_record.id,
    ]
    assert [item["severity"] for item in alert_summary["recent_open_items"]] == [
        "high",
        "warning",
        "info",
    ]
    assert all(item["href"] == f"/health/{item['source_record_id']}" for item in alert_summary["recent_open_items"])


def _create_expense_fact(
    db: Session,
    *,
    created_at: datetime,
    amount: str,
    currency: str,
    category: str | None,
    note: str | None,
    with_pending: bool,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        payload={"amount": amount, "currency": currency, "category": category, "note": note},
        with_pending=with_pending,
    )
    record = create_expense_record(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"amount": amount, "currency": currency, "category": category, "note": note},
    )
    record.created_at = created_at
    db.commit()
    return record


def _create_knowledge_fact(
    db: Session,
    *,
    created_at: datetime,
    title: str | None,
    content: str | None,
    source_text: str | None,
    with_pending: bool,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        payload={"title": title, "content": content, "source_text": source_text},
        with_pending=with_pending,
    )
    entry = create_knowledge_entry(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"title": title, "content": content, "source_text": source_text},
    )
    entry.created_at = created_at
    db.commit()
    return entry


def _create_health_fact(
    db: Session,
    *,
    created_at: datetime,
    metric_type: str,
    value_text: str | None,
    note: str | None,
    with_pending: bool,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        payload={"metric_type": metric_type, "value_text": value_text, "note": note},
        with_pending=with_pending,
    )
    record = create_health_record(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"metric_type": metric_type, "value_text": value_text, "note": note},
    )
    record.created_at = created_at
    db.commit()
    return record


def _create_pending_item(
    db: Session,
    *,
    target_domain: str,
    status: str,
    created_at: datetime,
    resolved_at: datetime | None = None,
):
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.PENDING if status == PendingStatus.OPEN else CaptureStatus.COMMITTED,
    )
    if resolved_at is not None:
        capture.finalized_at = resolved_at

    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.8,
        confidence_level=ParseConfidenceLevel.MEDIUM,
        parsed_payload_json={"target_domain": target_domain},
        parser_name="test",
        parser_version="0.1.0",
    )

    pending_item = pending_repository.create_pending_item(
        db,
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=target_domain,
        proposed_payload_json={"target_domain": target_domain},
        reason="fixture",
        status=status,
    )
    pending_item.created_at = created_at
    pending_item.resolved_at = resolved_at
    db.commit()
    return pending_item


def _create_pending_review_action(
    db: Session,
    *,
    pending_item_id: int,
    action_type: str,
    note: str | None,
    created_at: datetime,
):
    action = pending_repository.create_pending_review_action(
        db,
        pending_item_id=pending_item_id,
        action_type=action_type,
        note=note,
    )
    action.created_at = created_at
    db.commit()
    return action


def _create_lineage(
    db: Session,
    *,
    target_domain: str,
    payload: dict[str, object | None],
    with_pending: bool,
) -> tuple[int, int | None]:
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.9,
        confidence_level=ParseConfidenceLevel.HIGH,
        parsed_payload_json=payload,
        parser_name="test",
        parser_version="0.1.0",
    )
    pending_id = None
    if with_pending:
        pending = pending_repository.create_pending_item(
            db,
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            target_domain=target_domain,
            proposed_payload_json=payload,
            reason="fixture",
        )
        pending_id = pending.id
    db.flush()
    return capture.id, pending_id
