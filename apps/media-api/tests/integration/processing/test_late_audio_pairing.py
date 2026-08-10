from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest
from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import ScanCandidate
from media_api.intake.service import ScanService
from media_api.processing.processors.mp4_m4a import Mp4M4aMergeProcessor
from media_api.processing.models import ProcessingJob
from media_api.processing.planner import ProcessingCommandService
from media_api.processing.registry import ProcessorRegistry
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
def registry() -> ProcessorRegistry:
    return ProcessorRegistry([Mp4M4aMergeProcessor()])


def test_late_m4a_upgrades_uningested_mp4(
    session: Session,
    storage: LocalStorage,
    registry: ProcessorRegistry,
    copy_media_fixture,
) -> None:
    video = copy_media_fixture("paired.mp4", storage.inbox_dir)
    scanner = ScanService(session=session, storage=storage, registry=registry)
    scanner.scan()
    scanner.scan()
    item = scanner.list_items()[0]
    item_id = item.id
    assert item.status == "ready_to_tag"

    audio = copy_media_fixture("paired.m4a", storage.inbox_dir)
    scanner.scan()
    scanner.scan()
    upgraded = scanner.get_item(item_id)

    assert upgraded is not None
    assert upgraded.status == "processing"
    assert len(upgraded.sources) == 2
    assert {Path(source.canonical_path) for source in upgraded.sources} == {video, audio}


def test_late_m4a_stays_unclaimed_after_ingest_has_started(
    session: Session,
    storage: LocalStorage,
    registry: ProcessorRegistry,
    copy_media_fixture,
) -> None:
    copy_media_fixture("paired.mp4", storage.inbox_dir)
    scanner = ScanService(session=session, storage=storage, registry=registry)
    scanner.scan()
    scanner.scan()
    item = scanner.list_items()[0]
    IngestService(session=session, storage=storage).prepare(item.id, "active-ingest")
    audio = copy_media_fixture("paired.m4a", storage.inbox_dir)

    scanner.scan()
    scanner.scan()
    unchanged = scanner.get_item(item.id)
    audio_candidate = session.scalar(
        select(ScanCandidate).where(ScanCandidate.canonical_path == str(audio))
    )

    assert unchanged is not None
    assert unchanged.status == "ready_to_tag"
    assert len(unchanged.sources) == 1
    assert audio_candidate is not None
    assert audio_candidate.is_stable is True
    assert audio_candidate.is_claimed is False


def test_late_m4a_stays_unclaimed_after_asset_is_completed(
    session: Session,
    storage: LocalStorage,
    registry: ProcessorRegistry,
    copy_media_fixture,
) -> None:
    copy_media_fixture("paired.mp4", storage.inbox_dir)
    scanner = ScanService(session=session, storage=storage, registry=registry)
    scanner.scan()
    scanner.scan()
    item = scanner.list_items()[0]
    IngestService(session=session, storage=storage).ingest(item.id, "completed-ingest")
    audio = copy_media_fixture("paired.m4a", storage.inbox_dir)

    scanner.scan()
    scanner.scan()
    unchanged = scanner.get_item(item.id)
    audio_candidate = session.scalar(
        select(ScanCandidate).where(ScanCandidate.canonical_path == str(audio))
    )

    assert unchanged is not None
    assert unchanged.status == "completed"
    assert len(unchanged.sources) == 1
    assert audio_candidate is not None
    assert audio_candidate.is_stable is True
    assert audio_candidate.is_claimed is False


def test_non_auto_processor_claims_pair_but_waits_for_manual_command(
    session: Session,
    storage: LocalStorage,
    copy_media_fixture,
) -> None:
    processor = Mp4M4aMergeProcessor()
    processor.descriptor = replace(processor.descriptor, auto_run_default=False)
    manual_registry = ProcessorRegistry([processor])
    copy_media_fixture("paired.mp4", storage.inbox_dir)
    copy_media_fixture("paired.m4a", storage.inbox_dir)
    scanner = ScanService(
        session=session,
        storage=storage,
        registry=manual_registry,
    )

    scanner.scan()
    scanner.scan()
    item = scanner.list_items()[0]

    assert item.status == "discovered"
    assert len(item.sources) == 2
    assert session.query(ProcessingJob).count() == 0

    job = ProcessingCommandService(
        session=session,
        registry=manual_registry,
    ).create_job(item.id, "manual-pair")
    assert job.status == "queued"
    assert item.status == "processing"
