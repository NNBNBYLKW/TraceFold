from __future__ import annotations

import pytest
from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxStatusEvent, ScanCandidate
from media_api.intake.service import ScanService
from media_api.storage.local import LocalStorage


@pytest.fixture
def session(settings: BootstrapSettings) -> Session:
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    db = session_factory()
    try:
        yield db
    finally:
        db.close()
        Base.metadata.drop_all(engine)
        engine.dispose()


@pytest.fixture
def scan_service(session: Session, storage: LocalStorage) -> ScanService:
    return ScanService(session=session, storage=storage)


def test_scan_creates_one_item_after_repeated_refreshes(
    scan_service: ScanService,
    storage: LocalStorage,
) -> None:
    (storage.inbox_dir / "photo.jpg").write_bytes(b"image")

    first = scan_service.scan()
    second = scan_service.scan()
    third = scan_service.scan()

    assert first.created_items == 0
    assert second.created_items == 1
    assert third.created_items == 0
    items = scan_service.list_items()
    assert len(items) == 1
    assert items[0].status == "ready_to_tag"
    assert items[0].suggested_actual_filename == "photo.jpg"
    assert len(items[0].sources) == 1


def test_created_item_records_full_analysis_history_and_claims_candidate(
    scan_service: ScanService,
    storage: LocalStorage,
    session: Session,
) -> None:
    (storage.inbox_dir / "clip.mp4").write_bytes(b"video")
    scan_service.scan()
    scan_service.scan()

    item = scan_service.list_items()[0]
    events = session.scalars(
        select(InboxStatusEvent)
        .where(InboxStatusEvent.inbox_item_id == item.id)
        .order_by(InboxStatusEvent.created_at, InboxStatusEvent.id)
    ).all()
    candidate = session.scalar(select(ScanCandidate))

    assert [event.to_status for event in events] == [
        "discovered",
        "analyzing",
        "ready_to_tag",
    ]
    assert candidate is not None
    assert candidate.is_claimed is True
