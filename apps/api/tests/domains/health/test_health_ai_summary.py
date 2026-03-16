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
from app.domains.ai_derivations.service import list_ai_derivation_reads
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.domains.health import ai_summary
from app.domains.health.service import create_health_record, rerun_health_summary
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
        f"sqlite:///{tmp_path / 'health-ai-summary.db'}",
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


def test_only_subjective_health_records_generate_health_summary(db: Session) -> None:
    subjective_record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "headache after lunch",
            "note": "It faded by the evening.",
        },
    )
    hard_metric_record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "heart_rate",
            "value_text": "115",
            "note": "after walking",
        },
    )
    db.commit()

    subjective_results = list_ai_derivation_reads(
        db,
        target_domain="health",
        target_record_id=subjective_record.id,
    )
    hard_metric_results = list_ai_derivation_reads(
        db,
        target_domain="health",
        target_record_id=hard_metric_record.id,
    )

    assert len(subjective_results.items) == 1
    assert subjective_results.items[0].derivation_type == "health_summary"
    assert subjective_results.items[0].status == "completed"
    assert hard_metric_results.items == []


def test_health_summary_content_json_uses_strict_four_field_schema(db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "mild nausea",
            "note": "Started after a rushed meal.",
        },
    )
    db.commit()

    result = list_ai_derivation_reads(db, target_domain="health", target_record_id=record.id).items[0]

    assert set(result.content_json.keys()) == {
        "summary",
        "observations",
        "suggested_follow_up",
        "care_level_note",
    }
    assert isinstance(result.content_json["observations"], list)


def test_manual_rerun_overwrites_current_health_summary_without_history_chain(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "headache after lunch",
            "note": "It faded by the evening.",
        },
    )
    db.commit()

    existing = list_ai_derivation_reads(db, target_domain="health", target_record_id=record.id).items[0]
    monkeypatch.setattr(
        ai_summary,
        "build_health_summary_content",
        lambda health_record: {
            "summary": "Updated rerun summary.",
            "observations": ["Observation A."],
            "suggested_follow_up": (
                "Consider adding a little more context to this record, or noting whether similar patterns appear again."
            ),
            "care_level_note": (
                "This note is only a supportive interpretation and not a medical conclusion. "
                "If the same issue keeps appearing or becomes more concerning, consider seeking professional advice."
            ),
        },
    )

    rerun_result = rerun_health_summary(db, health_id=record.id)

    assert len(rerun_result.items) == 1
    assert rerun_result.items[0].id == existing.id
    assert rerun_result.items[0].status == "completed"
    assert rerun_result.items[0].content_json["summary"] == "Updated rerun summary."
    assert list_ai_derivation_reads(db, target_domain="health", target_record_id=record.id).items[0].id == existing.id


def test_failed_rerun_writes_failed_status_error_message_and_clears_content(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "tight chest feeling",
            "note": "More noticeable at night.",
        },
    )
    db.commit()

    existing = list_ai_derivation_reads(db, target_domain="health", target_record_id=record.id).items[0]
    monkeypatch.setattr(
        ai_summary,
        "build_health_summary_content",
        lambda health_record: (_ for _ in ()).throw(RuntimeError("Simulated derivation failure")),
    )

    rerun_result = rerun_health_summary(db, health_id=record.id)

    assert len(rerun_result.items) == 1
    assert rerun_result.items[0].id == existing.id
    assert rerun_result.items[0].status == "failed"
    assert rerun_result.items[0].error_message == "Simulated derivation failure"
    assert rerun_result.items[0].content_json is None
    assert rerun_result.items[0].failed_at is not None


def test_manual_single_rerun_endpoint_is_available_for_health_ai_summary(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "felt unusually tired",
            "note": "Happened after a stressful afternoon.",
        },
    )
    db.commit()

    monkeypatch.setattr(
        ai_summary,
        "build_health_summary_content",
        lambda health_record: {
            "summary": "Endpoint rerun summary.",
            "observations": ["Observation from endpoint rerun."],
            "suggested_follow_up": (
                "Consider adding a little more context to this record, or noting whether similar patterns appear again."
            ),
            "care_level_note": (
                "This note is only a supportive interpretation and not a medical conclusion. "
                "If the same issue keeps appearing or becomes more concerning, consider seeking professional advice."
            ),
        },
    )

    response = api_client.post(f"/api/health/{record.id}/ai/health-summary/rerun")

    assert response.status_code == 200
    items = response.json()["data"]["items"]
    assert len(items) == 1
    assert items[0]["derivation_type"] == "health_summary"
    assert items[0]["status"] == "completed"
    assert items[0]["content_json"]["summary"] == "Endpoint rerun summary."


def test_health_summary_copy_stays_within_supportive_non_medical_boundary(db: Session) -> None:
    record = create_health_record(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "metric_type": "symptom",
            "value_text": "stomach discomfort",
            "note": "Noticed after dinner.",
        },
    )
    db.commit()

    result = list_ai_derivation_reads(db, target_domain="health", target_record_id=record.id).items[0]
    suggested_follow_up = result.content_json["suggested_follow_up"].lower()
    care_level_note = result.content_json["care_level_note"].lower()

    assert "treatment" not in suggested_follow_up
    assert "medication" not in suggested_follow_up
    assert "prescription" not in suggested_follow_up
    assert "diagnos" not in care_level_note
    assert "risk level" not in care_level_note
    assert "medical conclusion" in care_level_note


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
