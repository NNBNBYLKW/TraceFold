from __future__ import annotations

from collections.abc import Generator
from datetime import datetime
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.alerts.service import upsert_alert_result
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
        f"sqlite:///{tmp_path / 'alert-consumption.db'}",
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


def test_health_alerts_can_be_read_for_health_module(api_client: TestClient, db: Session) -> None:
    older_high = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=21,
        rule_code="HEALTH_HEART_RATE_HIGH_V1",
        severity="high",
        status="open",
        title="High heart rate",
        message="Older but higher priority.",
        explanation="This record is well outside the usual range and should not be ignored. Please consider checking the situation promptly.",
        triggered_at=datetime(2026, 3, 16, 9, 0, 0),
    )
    newer_info = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=22,
        rule_code="HEALTH_BLOOD_PRESSURE_INFO_V1",
        severity="info",
        status="open",
        title="Info blood pressure",
        message="Newer but lower priority.",
        explanation="This record is slightly outside the preferred range. It may be worth keeping an eye on if similar entries appear again.",
        triggered_at=datetime(2026, 3, 16, 11, 0, 0),
    )
    upsert_alert_result(
        db,
        source_domain="expense",
        source_record_id=99,
        rule_code="EXPENSE_TEST_INFO_V1",
        severity="info",
        status="open",
        title="Should not leak",
        message="Not a health alert.",
        explanation=None,
        triggered_at=datetime(2026, 3, 16, 12, 0, 0),
    )
    db.commit()

    response = api_client.get("/api/alerts", params={"source_domain": "health"})

    assert response.status_code == 200
    items = response.json()["data"]["items"]
    assert [item["id"] for item in items] == [older_high.id, newer_info.id]
    assert [item["source_record_id"] for item in items] == [21, 22]
    assert items[0]["severity"] == "high"
    assert items[1]["severity"] == "info"


def test_alert_viewed_and_dismissed_status_transitions_are_supported(api_client: TestClient, db: Session) -> None:
    alert = upsert_alert_result(
        db,
        source_domain="health",
        source_record_id=33,
        rule_code="HEALTH_SLEEP_DURATION_WARNING_V1",
        severity="warning",
        status="open",
        title="Short sleep duration",
        message="Needs review.",
        explanation="This record is noticeably outside the usual range and may deserve attention. Consider reviewing the context of this entry.",
        triggered_at=datetime(2026, 3, 16, 10, 0, 0),
    )
    db.commit()

    viewed_response = api_client.post(f"/api/alerts/{alert.id}/viewed")
    dismissed_response = api_client.post(f"/api/alerts/{alert.id}/dismissed")

    assert viewed_response.status_code == 200
    assert viewed_response.json()["data"]["status"] == "viewed"
    assert viewed_response.json()["data"]["viewed_at"] is not None
    assert dismissed_response.status_code == 200
    assert dismissed_response.json()["data"]["status"] == "dismissed"
    assert dismissed_response.json()["data"]["dismissed_at"] is not None
