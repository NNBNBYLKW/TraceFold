from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.domains.ai_derivations import service as ai_derivations_service
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.domains.knowledge.service import create_knowledge_entry


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
    import app.domains.workbench.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'ai-derivation-service.db'}",
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


def test_generate_knowledge_summary_now_persists_formal_derivation_basis(db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Derivation basis note",
            "content": "The derivation row should store a compact source-basis fingerprint.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    derivation = ai_derivations_service.get_ai_derivation_read(
        db,
        target_type="knowledge",
        target_id=entry.id,
    )

    assert derivation.derivation_kind == "knowledge_summary"
    assert derivation.status == "ready"
    assert derivation.model_key == "qwen3.5:9b"
    assert derivation.model_version == "openai_compatible:knowledge_summary_json_v3"
    assert derivation.source_basis_json["target_type"] == "knowledge"
    assert derivation.source_basis_json["target_id"] == entry.id
    assert derivation.source_basis_json["derivation_kind"] == "knowledge_summary"
    assert derivation.source_basis_json["content_fingerprint"]
    assert derivation.source_basis_json["source_updated_at"] is not None


def test_invalidate_ai_derivation_marks_current_row_invalidated(db: Session) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Invalidate me",
            "content": "Current derivation should become invalidated without deleting the formal fact.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    invalidated = ai_derivations_service.invalidate_ai_derivation(
        db,
        target_type="knowledge",
        target_id=entry.id,
    )
    db.commit()

    derivation = ai_derivations_service.get_ai_derivation_read(
        db,
        target_type="knowledge",
        target_id=entry.id,
    )

    assert invalidated.status == "invalidated"
    assert invalidated.invalidated_at is not None
    assert derivation.status == "invalidated"
    assert derivation.invalidated_at is not None
    assert derivation.content_json["summary"]


def test_generate_knowledge_summary_failure_marks_failed(db: Session, monkeypatch: pytest.MonkeyPatch) -> None:
    entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db).id,
        source_pending_id=None,
        payload={
            "title": "Failure note",
            "content": "A derivation failure should stay visible on the derivation row.",
            "source_text": "Seed text.",
        },
    )
    db.commit()

    monkeypatch.setattr(
        "app.domains.knowledge.ai_summary.build_knowledge_summary_content",
        lambda knowledge_entry: (_ for _ in ()).throw(RuntimeError("derivation exploded")),
    )

    with pytest.raises(Exception):
        ai_derivations_service.generate_knowledge_summary_now(
            db,
            knowledge_entry=entry,
            raise_on_failure=True,
        )

    derivation = ai_derivations_service.get_ai_derivation_read(
        db,
        target_type="knowledge",
        target_id=entry.id,
    )

    assert derivation.status == "failed"
    assert derivation.error_message == "derivation exploded"
    assert derivation.content_json is None


def _create_capture(db: Session):
    return capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
