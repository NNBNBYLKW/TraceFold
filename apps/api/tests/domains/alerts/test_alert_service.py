from __future__ import annotations

from collections.abc import Generator
from datetime import datetime
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.exceptions import IllegalStateError
from app.db.base import Base
from app.domains.alerts import service


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
        f"sqlite:///{tmp_path / 'alert-service.db'}",
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


def test_resolve_from_open_persists_resolution_note(db: Session) -> None:
    alert = service.upsert_rule_alert(
        db,
        domain="health",
        rule_key="HEALTH_HEART_RATE_HIGH_V1",
        severity="high",
        source_record_type="health_record",
        source_record_id=11,
        message="Needs review.",
        details_json={"title": "High heart rate"},
        triggered_at=datetime(2026, 3, 23, 10, 0, 0),
    )
    db.commit()

    resolved = service.resolve_alert(
        db,
        alert_id=alert.id,
        resolution_note="Reviewed with current context.",
    )

    assert resolved.status == "resolved"
    assert resolved.resolution_note == "Reviewed with current context."
    assert resolved.resolved_at is not None


def test_acknowledge_after_resolution_is_illegal(db: Session) -> None:
    alert = service.upsert_rule_alert(
        db,
        domain="health",
        rule_key="HEALTH_SLEEP_DURATION_WARNING_V1",
        severity="warning",
        source_record_type="health_record",
        source_record_id=12,
        message="Needs review.",
        details_json={"title": "Short sleep duration"},
        triggered_at=datetime(2026, 3, 23, 11, 0, 0),
        status="resolved",
        resolved_at=datetime(2026, 3, 23, 11, 5, 0),
    )
    db.commit()

    with pytest.raises(IllegalStateError) as exc_info:
        service.acknowledge_alert(db, alert_id=alert.id)

    assert exc_info.value.code == "ALERT_STATE_CONFLICT"
