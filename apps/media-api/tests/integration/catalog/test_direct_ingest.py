from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, MediaAsset
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage
from media_api.taxonomy.models import Author, Series, Tag


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
def source_path(storage: LocalStorage) -> Path:
    path = storage.inbox_dir / "direct.mp4"
    path.write_bytes(b"direct-video")
    return path


@pytest.fixture
def ready_video(
    session: Session,
    storage: LocalStorage,
    source_path: Path,
) -> InboxItem:
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source_path.name,
        display_name="Direct video",
    )
    item.authors = [
        Author(normalized_name="alice", display_name="Alice"),
        Author(normalized_name="bob", display_name="Bob"),
    ]
    item.series = Series(normalized_name="city walk", display_name="City Walk")
    item.tags = [
        Tag(normalized_name="night", display_name="night"),
        Tag(normalized_name="travel", display_name="travel"),
    ]
    item.sources.append(
        SourceFile.from_snapshot(storage.snapshot(source_path), is_primary=True)
    )
    session.add(item)
    session.commit()
    return item


@pytest.fixture
def ingest_service(session: Session, storage: LocalStorage) -> IngestService:
    return IngestService(session=session, storage=storage)


def test_prepare_copies_verified_source_without_deleting_it(
    ingest_service: IngestService,
    ready_video: InboxItem,
    source_path: Path,
    storage: LocalStorage,
) -> None:
    operation = ingest_service.prepare(
        item_id=ready_video.id,
        idempotency_key="ingest-1",
    )

    assert operation.state == "prepared"
    assert source_path.exists()
    assert operation.staged_path is not None
    assert Path(operation.staged_path).exists()
    assert storage.sha256(Path(operation.staged_path)) == operation.source_hash
    assert ready_video.sources[0].content_sha256 == operation.source_hash


def test_ingest_moves_file_and_persists_metadata(
    ingest_service: IngestService,
    ready_video: InboxItem,
    source_path: Path,
    session: Session,
) -> None:
    result = ingest_service.ingest(ready_video.id, "ingest-complete")
    asset = session.get(MediaAsset, result.asset_id)
    operation = session.get(IngestOperation, result.operation_id)

    assert result.kind == "completed"
    assert result.cleanup_warning is None
    assert asset is not None
    assert asset.actual_filename == source_path.name
    assert asset.final_relative_path == source_path.name
    assert asset.display_name == ready_video.display_name
    assert {author.id for author in asset.authors} == {
        author.id for author in ready_video.authors
    }
    assert asset.series_id == ready_video.series_id
    assert {tag.id for tag in asset.tags} == {tag.id for tag in ready_video.tags}
    assert ready_video.status == "completed"
    assert operation is not None
    assert operation.state == "completed"
    assert not source_path.exists()
    assert (ingest_service.storage.library_dir / source_path.name).exists()
    assert not (ingest_service.storage.staging_dir / operation.id).exists()


def test_name_conflict_is_recorded_before_staging_and_preserves_both_files(
    ingest_service: IngestService,
    ready_video: InboxItem,
    source_path: Path,
    storage: LocalStorage,
    session: Session,
) -> None:
    existing = storage.library_dir / source_path.name
    existing.write_bytes(b"existing-formal-video")

    result = ingest_service.ingest(ready_video.id, "ingest-conflict")
    operation = session.get(IngestOperation, result.operation_id)

    assert result.kind == "conflict"
    assert result.asset_id is None
    assert result.conflict_id == result.operation_id
    assert operation is not None
    assert operation.conflict_kinds_json == ["name"]
    assert operation.staged_path is None
    assert ready_video.status == "conflict"
    assert source_path.read_bytes() == b"direct-video"
    assert existing.read_bytes() == b"existing-formal-video"
    assert not (storage.staging_dir / operation.id).exists()
