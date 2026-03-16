from __future__ import annotations

from collections.abc import Generator
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
from app.domains.knowledge import ai_summary
from app.domains.knowledge.service import create_knowledge_entry, rerun_knowledge_summary
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
        f"sqlite:///{tmp_path / 'knowledge-ai-summary.db'}",
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


def test_knowledge_summary_is_generated_automatically_after_knowledge_write(db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Morning routine note",
            "content": "I feel more focused when I prepare the next day before going to sleep.",
            "source_text": "Captured from a quick journal draft.",
        },
    )
    db.commit()

    results = list_ai_derivation_reads(db, target_domain="knowledge", target_record_id=entry.id)

    assert len(results.items) == 1
    assert results.items[0].derivation_type == "knowledge_summary"
    assert results.items[0].status == "completed"


def test_knowledge_summary_content_json_uses_strict_three_field_schema(db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Reading note",
            "content": "Short summaries help me remember the argument structure of longer essays.",
            "source_text": "Excerpt from a reading log.",
        },
    )
    db.commit()

    result = list_ai_derivation_reads(db, target_domain="knowledge", target_record_id=entry.id).items[0]

    assert set(result.content_json.keys()) == {"summary", "key_points", "keywords"}
    assert isinstance(result.content_json["key_points"], list)
    assert isinstance(result.content_json["keywords"], list)


def test_manual_rerun_overwrites_current_knowledge_summary_without_history_chain(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Project note",
            "content": "The smaller the unit of review, the easier it is to keep momentum.",
            "source_text": "Internal reflection.",
        },
    )
    db.commit()

    existing = list_ai_derivation_reads(db, target_domain="knowledge", target_record_id=entry.id).items[0]
    monkeypatch.setattr(
        ai_summary,
        "build_knowledge_summary_content",
        lambda knowledge_entry: {
            "summary": "Updated knowledge summary.",
            "key_points": ["Updated point one.", "Updated point two."],
            "keywords": ["Updated", "summary"],
        },
    )

    rerun_result = rerun_knowledge_summary(db, knowledge_id=entry.id)

    assert len(rerun_result.items) == 1
    assert rerun_result.items[0].id == existing.id
    assert rerun_result.items[0].status == "completed"
    assert rerun_result.items[0].content_json["summary"] == "Updated knowledge summary."


def test_failed_rerun_writes_failed_status_error_message_and_clears_content(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Failure case note",
            "content": "This entry is only here to exercise the failure path.",
            "source_text": "Source excerpt.",
        },
    )
    db.commit()

    existing = list_ai_derivation_reads(db, target_domain="knowledge", target_record_id=entry.id).items[0]
    monkeypatch.setattr(
        ai_summary,
        "build_knowledge_summary_content",
        lambda knowledge_entry: (_ for _ in ()).throw(RuntimeError("Simulated knowledge summary failure")),
    )

    rerun_result = rerun_knowledge_summary(db, knowledge_id=entry.id)

    assert len(rerun_result.items) == 1
    assert rerun_result.items[0].id == existing.id
    assert rerun_result.items[0].status == "failed"
    assert rerun_result.items[0].error_message == "Simulated knowledge summary failure"
    assert rerun_result.items[0].content_json is None
    assert rerun_result.items[0].failed_at is not None


def test_manual_single_rerun_endpoint_is_available_for_knowledge_ai_summary(
    api_client: TestClient,
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Endpoint note",
            "content": "A concise summary makes retrieval easier later.",
            "source_text": "Taken from a note review.",
        },
    )
    db.commit()

    monkeypatch.setattr(
        ai_summary,
        "build_knowledge_summary_content",
        lambda knowledge_entry: {
            "summary": "Endpoint knowledge summary.",
            "key_points": ["Endpoint point."],
            "keywords": ["Endpoint", "knowledge"],
        },
    )

    response = api_client.post(f"/api/knowledge/{entry.id}/ai/knowledge-summary/rerun")

    assert response.status_code == 200
    items = response.json()["data"]["items"]
    assert len(items) == 1
    assert items[0]["derivation_type"] == "knowledge_summary"
    assert items[0]["status"] == "completed"
    assert items[0]["content_json"]["summary"] == "Endpoint knowledge summary."


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
