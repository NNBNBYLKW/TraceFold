from __future__ import annotations

from collections.abc import Generator
from datetime import datetime
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.exceptions import BadRequestError
from app.db.base import Base
from app.db.session import get_db
from app.domains.ai_derivations.service import list_ai_derivation_reads, upsert_ai_derivation_result
from app.domains.alerts.service import list_alert_reads, upsert_alert_result
from app.main import app


@pytest.fixture
def db(tmp_path: Path) -> Generator[Session, None, None]:
    import app.domains.ai_derivations.models  # noqa: F401
    import app.domains.alerts.models  # noqa: F401
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'enhancement-skeleton.db'}",
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


def test_alert_result_can_be_created_and_read_by_source(api_client: TestClient, db: Session) -> None:
    alert = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=101,
        rule_code="HEALTH_HEART_RATE_HIGH_V1",
        severity="high",
        status="open",
        title="Heart rate reminder",
        message="Recent heart rate value is notably high.",
        explanation="This is a factual reminder tied to one health record.",
        triggered_at=datetime(2026, 3, 16, 8, 0, 0),
    )
    db.commit()

    response = api_client.get(
        "/api/alerts",
        params={"source_domain": "health", "source_record_id": 101},
    )

    assert response.status_code == 200
    data = response.json()["data"]["items"]
    assert len(data) == 1
    assert data[0] == {
        "id": alert.id,
        "source_domain": "health",
        "source_record_id": 101,
        "rule_code": "HEALTH_HEART_RATE_HIGH_V1",
        "severity": "high",
        "status": "open",
        "title": "Heart rate reminder",
        "message": "Recent heart rate value is notably high.",
        "explanation": "This is a factual reminder tied to one health record.",
        "triggered_at": "2026-03-16T08:00:00",
        "viewed_at": None,
        "dismissed_at": None,
        "created_at": data[0]["created_at"],
    }


def test_ai_derivation_result_can_be_created_and_read_by_target(api_client: TestClient, db: Session) -> None:
    derivation = upsert_ai_derivation_result(
        db,
        target_domain="knowledge",
        target_record_id=202,
        derivation_type="knowledge_summary",
        status="completed",
        model_name="gpt-test",
        model_version="0.1.0",
        generated_at=datetime(2026, 3, 16, 9, 30, 0),
        content_json={
            "summary": "Short summary",
            "key_points": ["A", "B"],
            "keywords": ["tracefold", "knowledge"],
        },
    )
    db.commit()

    response = api_client.get(
        "/api/ai-derivations",
        params={"target_domain": "knowledge", "target_record_id": 202},
    )

    assert response.status_code == 200
    data = response.json()["data"]["items"]
    assert len(data) == 1
    assert data[0] == {
        "id": derivation.id,
        "target_domain": "knowledge",
        "target_record_id": 202,
        "derivation_type": "knowledge_summary",
        "status": "completed",
        "model_name": "gpt-test",
        "model_version": "0.1.0",
        "generated_at": "2026-03-16T09:30:00",
        "failed_at": None,
        "content_json": {
            "summary": "Short summary",
            "key_points": ["A", "B"],
            "keywords": ["tracefold", "knowledge"],
        },
        "error_message": None,
        "created_at": data[0]["created_at"],
    }


def test_alert_upsert_keeps_single_current_row_per_rule_key(db: Session) -> None:
    first = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=303,
        rule_code="HEALTH_SLEEP_SHORT_V1",
        severity="warning",
        status="open",
        title="Sleep reminder",
        message="Sleep duration is shorter than usual.",
        explanation="Initial run.",
        triggered_at=datetime(2026, 3, 16, 10, 0, 0),
    )
    db.commit()

    second = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=303,
        rule_code="HEALTH_SLEEP_SHORT_V1",
        severity="high",
        status="viewed",
        title="Sleep reminder",
        message="Sleep duration is much shorter than usual.",
        explanation="Rerun updated the current result.",
        triggered_at=datetime(2026, 3, 16, 11, 0, 0),
        viewed_at=datetime(2026, 3, 16, 11, 5, 0),
    )
    db.commit()

    result = list_alert_reads(db, source_domain="health", source_record_id=303)

    assert second.id == first.id
    assert len(result.items) == 1
    assert result.items[0].severity == "high"
    assert result.items[0].status == "viewed"
    assert result.items[0].message == "Sleep duration is much shorter than usual."
    assert result.items[0].viewed_at == datetime(2026, 3, 16, 11, 5, 0)


def test_ai_derivation_upsert_keeps_single_current_row_per_target_type(db: Session) -> None:
    first = upsert_ai_derivation_result(
        db,
        target_domain="health",
        target_record_id=404,
        derivation_type="health_summary",
        status="pending",
        model_name="gpt-test",
        model_version="0.1.0",
    )
    db.commit()

    second = upsert_ai_derivation_result(
        db,
        target_domain="health",
        target_record_id=404,
        derivation_type="health_summary",
        status="failed",
        model_name="gpt-test",
        model_version="0.1.1",
        failed_at=datetime(2026, 3, 16, 12, 15, 0),
        content_json=None,
        error_message="Upstream timeout",
    )
    db.commit()

    result = list_ai_derivation_reads(db, target_domain="health", target_record_id=404)

    assert second.id == first.id
    assert len(result.items) == 1
    assert result.items[0].status == "failed"
    assert result.items[0].model_version == "0.1.1"
    assert result.items[0].failed_at == datetime(2026, 3, 16, 12, 15, 0)
    assert result.items[0].error_message == "Upstream timeout"


@pytest.mark.parametrize(
    ("func", "kwargs", "code"),
    [
        (
            upsert_alert_result,
            {
                "source_domain": "health",
                "source_record_id": 1,
                "rule_code": "HEALTH_HEART_RATE_HIGH_V1",
                "severity": "info",
                "status": "archived",
                "title": "Invalid status",
                "message": "Should fail.",
                "triggered_at": datetime(2026, 3, 16, 13, 0, 0),
            },
            "INVALID_ALERT_STATUS",
        ),
        (
            upsert_ai_derivation_result,
            {
                "target_domain": "knowledge",
                "target_record_id": 2,
                "derivation_type": "knowledge_summary",
                "status": "done",
            },
            "INVALID_AI_DERIVATION_STATUS",
        ),
    ],
)
def test_status_enum_boundaries_are_enforced(db: Session, func, kwargs: dict[str, object], code: str) -> None:
    with pytest.raises(BadRequestError) as exc_info:
        func(db, **kwargs)

    assert exc_info.value.code == code


def test_ai_derivation_fields_round_trip_stably(db: Session) -> None:
    upsert_ai_derivation_result(
        db,
        target_domain="knowledge",
        target_record_id=505,
        derivation_type="knowledge_summary",
        status="failed",
        model_name="gpt-test",
        model_version="0.2.0",
        generated_at=datetime(2026, 3, 16, 14, 0, 0),
        failed_at=datetime(2026, 3, 16, 14, 5, 0),
        content_json={"summary": "temporary"},
        error_message="Model response schema mismatch",
    )
    db.commit()

    result = list_ai_derivation_reads(db, target_domain="knowledge", target_record_id=505)

    assert len(result.items) == 1
    assert result.items[0].content_json == {"summary": "temporary"}
    assert result.items[0].error_message == "Model response schema mismatch"
    assert result.items[0].generated_at == datetime(2026, 3, 16, 14, 0, 0)
    assert result.items[0].failed_at == datetime(2026, 3, 16, 14, 5, 0)
