from __future__ import annotations

from collections.abc import Generator
from datetime import datetime
from decimal import Decimal
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.alerts.service import list_alert_reads, upsert_alert_result
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus, ParseConfidenceLevel, ParseTargetDomain
from app.domains.health.models import HealthRecord
from app.domains.health.rules import (
    evaluate_health_rule_match,
    parse_blood_pressure_value,
    parse_heart_rate_value,
    parse_sleep_duration_value,
)
from app.domains.health.service import create_health_record, rerun_health_rules
from app.domains.pending import repository as pending_repository
from app.domains.pending.service import confirm_pending_item
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
        f"sqlite:///{tmp_path / 'health-rules.db'}",
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


@pytest.mark.parametrize(
    ("parser", "value_text", "expected"),
    [
        (parse_heart_rate_value, "105", Decimal("105")),
        (parse_sleep_duration_value, "360", Decimal("360")),
        (parse_blood_pressure_value, "120/80", (120, 80)),
    ],
)
def test_supported_metric_formats_parse_correctly(parser, value_text: str, expected) -> None:
    assert parser(value_text) == expected


@pytest.mark.parametrize(
    ("metric_type", "value_text"),
    [
        ("heart_rate", "110 bpm"),
        ("sleep_duration", "6 hours"),
        ("blood_pressure", "120-80"),
    ],
)
def test_invalid_formats_do_not_trigger_alert_results(
    db: Session,
    metric_type: str,
    value_text: str,
) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={"metric_type": metric_type, "value_text": value_text},
    )
    db.commit()

    results = list_alert_reads(db, source_domain="health", source_record_id=record.id)

    assert results.items == []


@pytest.mark.parametrize(
    ("metric_type", "value_text", "expected_rule_code", "expected_severity", "expected_explanation"),
    [
        (
            "heart_rate",
            "105",
            "HEALTH_HEART_RATE_INFO_V1",
            "info",
            "This record is slightly outside the preferred range. It may be worth keeping an eye on if similar entries appear again.",
        ),
        (
            "heart_rate",
            "115",
            "HEALTH_HEART_RATE_WARNING_V1",
            "warning",
            "This record is noticeably outside the usual range and may deserve attention. Consider reviewing the context of this entry.",
        ),
        (
            "heart_rate",
            "135",
            "HEALTH_HEART_RATE_HIGH_V1",
            "high",
            "This record is well outside the usual range and should not be ignored. Please consider checking the situation promptly.",
        ),
        (
            "sleep_duration",
            "400",
            "HEALTH_SLEEP_DURATION_INFO_V1",
            "info",
            "This record is slightly outside the preferred range. It may be worth keeping an eye on if similar entries appear again.",
        ),
        (
            "sleep_duration",
            "320",
            "HEALTH_SLEEP_DURATION_WARNING_V1",
            "warning",
            "This record is noticeably outside the usual range and may deserve attention. Consider reviewing the context of this entry.",
        ),
        (
            "sleep_duration",
            "250",
            "HEALTH_SLEEP_DURATION_HIGH_V1",
            "high",
            "This record is well outside the usual range and should not be ignored. Please consider checking the situation promptly.",
        ),
        (
            "blood_pressure",
            "135/82",
            "HEALTH_BLOOD_PRESSURE_INFO_V1",
            "info",
            "This record is slightly outside the preferred range. It may be worth keeping an eye on if similar entries appear again.",
        ),
        (
            "blood_pressure",
            "145/95",
            "HEALTH_BLOOD_PRESSURE_WARNING_V1",
            "warning",
            "This record is noticeably outside the usual range and may deserve attention. Consider reviewing the context of this entry.",
        ),
        (
            "blood_pressure",
            "182/121",
            "HEALTH_BLOOD_PRESSURE_HIGH_V1",
            "high",
            "This record is well outside the usual range and should not be ignored. Please consider checking the situation promptly.",
        ),
    ],
)
def test_severity_ranges_are_evaluated_correctly(
    metric_type: str,
    value_text: str,
    expected_rule_code: str,
    expected_severity: str,
    expected_explanation: str,
) -> None:
    result = evaluate_health_rule_match(metric_type=metric_type, value_text=value_text)

    assert result is not None
    assert result.rule_code == expected_rule_code
    assert result.severity == expected_severity
    assert result.explanation == expected_explanation
    assert "diagnos" not in result.message.lower()
    assert "must " not in result.message.lower()


def test_rerun_matching_rule_updates_current_result_instead_of_inserting_duplicate(db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={"metric_type": "heart_rate", "value_text": "135"},
    )
    db.commit()

    existing = list_alert_reads(db, source_domain="health", source_record_id=record.id).items[0]
    upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=record.id,
        rule_code=existing.rule_code,
        severity=existing.severity,
        status="dismissed",
        title=existing.title,
        message=existing.message,
        explanation=existing.explanation,
        triggered_at=datetime(2026, 3, 16, 8, 0, 0),
        viewed_at=datetime(2026, 3, 16, 8, 5, 0),
        dismissed_at=datetime(2026, 3, 16, 8, 10, 0),
    )
    db.commit()

    results = rerun_health_rules(db, health_id=record.id)

    assert len(results.items) == 1
    assert results.items[0].id == existing.id
    assert results.items[0].status == "open"
    assert results.items[0].viewed_at is None
    assert results.items[0].dismissed_at is None
    assert results.items[0].triggered_at > datetime(2026, 3, 16, 8, 0, 0)


def test_rerun_removes_alert_when_record_no_longer_matches_any_rule(db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={"metric_type": "heart_rate", "value_text": "135"},
    )
    db.commit()

    record.value_text = "95"
    results = rerun_health_rules(db, health_id=record.id)

    assert results.items == []
    assert list_alert_reads(db, source_domain="health", source_record_id=record.id).items == []


def test_automatic_rule_execution_runs_after_health_record_write(db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={"metric_type": "sleep_duration", "value_text": "320"},
    )
    db.commit()

    results = list_alert_reads(db, source_domain="health", source_record_id=record.id)

    assert len(results.items) == 1
    assert results.items[0].rule_code == "HEALTH_SLEEP_DURATION_WARNING_V1"


def test_confirm_pending_health_record_triggers_rules_automatically(db: Session) -> None:
    pending_item = _create_health_pending(
        db,
        payload={"metric_type": "blood_pressure", "value_text": "145/95"},
    )

    confirm_pending_item(db, pending_item_id=pending_item.id)
    record = db.query(HealthRecord).one()
    results = list_alert_reads(db, source_domain="health", source_record_id=record.id)

    assert len(results.items) == 1
    assert results.items[0].rule_code == "HEALTH_BLOOD_PRESSURE_WARNING_V1"


def test_manual_single_rerun_endpoint_is_available(api_client: TestClient, db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={"metric_type": "heart_rate", "value_text": "105"},
    )
    db.commit()

    record.value_text = "135"
    db.commit()

    response = api_client.post(f"/api/health/{record.id}/rules/rerun")

    assert response.status_code == 200
    items = response.json()["data"]["items"]
    assert len(items) == 1
    assert items[0]["rule_code"] == "HEALTH_HEART_RATE_HIGH_V1"
    assert items[0]["severity"] == "high"


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )


def _create_health_pending(db: Session, *, payload: dict[str, str]):
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.PENDING,
    )
    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=ParseTargetDomain.HEALTH,
        confidence_score=0.7,
        confidence_level=ParseConfidenceLevel.HIGH,
        parsed_payload_json=payload,
        parser_name="test",
        parser_version="0.1.0",
    )
    pending_item = pending_repository.create_pending_item(
        db,
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=ParseTargetDomain.HEALTH,
        proposed_payload_json=payload,
        reason="fixture",
    )
    db.commit()
    return pending_item
