from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.catalog.conflicts import (
    ConflictChoiceExpired,
    ConflictReplacementUnavailable,
    ConflictResolution,
    ConflictService,
)
from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState, MediaAsset
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage
from media_api.taxonomy.models import Author, Series, Tag


@dataclass
class ConflictCase:
    operation: IngestOperation
    item: InboxItem
    source: Path
    existing_asset: MediaAsset
    existing_path: Path


@pytest.fixture
def session(settings: BootstrapSettings) -> Session:
    factory = create_engine_and_session(settings)
    engine = factory.kw["bind"]
    Base.metadata.create_all(engine)
    db = factory()
    try:
        yield db
    finally:
        db.close()
        Base.metadata.drop_all(engine)
        engine.dispose()


def _conflict_case(
    session: Session,
    storage: LocalStorage,
    *,
    incoming_bytes: bytes = b"incoming-video",
    existing_bytes: bytes = b"existing-video",
) -> ConflictCase:
    existing_path = storage.library_dir / "same.mp4"
    existing_path.write_bytes(existing_bytes)
    existing = MediaAsset(
        media_type="video",
        actual_filename=existing_path.name,
        final_relative_path=existing_path.name,
        display_name="Old display",
        size=len(existing_bytes),
        sha256=storage.sha256(existing_path),
    )
    source = storage.inbox_dir / existing_path.name
    source.write_bytes(incoming_bytes)
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
        display_name="Incoming display",
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    session.add_all([existing, item])
    session.commit()
    result = IngestService(session=session, storage=storage).ingest(
        item.id, f"conflict-{item.id}"
    )
    operation = session.get(IngestOperation, result.operation_id)
    assert operation is not None
    return ConflictCase(operation, item, source, existing, existing_path)


def test_identical_hash_still_requires_user_choice(
    session: Session,
    storage: LocalStorage,
) -> None:
    case = _conflict_case(
        session,
        storage,
        incoming_bytes=b"identical",
        existing_bytes=b"identical",
    )

    assert case.operation.conflict_kinds_json == ["name", "content"]
    assert case.item.status is InboxStatus.CONFLICT
    assert case.source.exists()
    assert case.existing_path.exists()


def test_skip_retains_sources_and_returns_item_to_ready(
    session: Session,
    storage: LocalStorage,
) -> None:
    case = _conflict_case(session, storage)

    result = ConflictService(session=session, storage=storage).resolve(
        case.operation.id,
        ConflictResolution.SKIP,
        idempotency_key="skip-once",
    )

    assert result.resolution is ConflictResolution.SKIP
    assert result.kind == "skipped"
    assert case.item.status is InboxStatus.READY_TO_TAG
    assert case.operation.state is IngestOperationState.SKIPPED
    assert case.source.read_bytes() == b"incoming-video"
    assert case.existing_path.read_bytes() == b"existing-video"
    assert session.query(MediaAsset).count() == 1


def test_keep_both_uses_suffix_without_changing_display_name(
    session: Session,
    storage: LocalStorage,
) -> None:
    case = _conflict_case(session, storage)

    result = ConflictService(session=session, storage=storage).resolve(
        case.operation.id,
        ConflictResolution.KEEP_BOTH,
        idempotency_key="keep-both-once",
    )
    incoming_asset = session.get(MediaAsset, result.asset_id)

    assert result.kind == "completed"
    assert result.resolution is ConflictResolution.KEEP_BOTH
    assert result.final_path is not None
    assert result.final_path.name == "same (2).mp4"
    assert incoming_asset is not None
    assert incoming_asset.display_name == "Incoming display"
    assert incoming_asset.actual_filename == "same (2).mp4"
    assert case.existing_path.read_bytes() == b"existing-video"
    assert result.final_path.read_bytes() == b"incoming-video"
    assert not case.source.exists()
    assert session.query(MediaAsset).count() == 2


def test_replace_retains_asset_id_and_uses_incoming_metadata(
    session: Session,
    storage: LocalStorage,
) -> None:
    case = _conflict_case(session, storage)
    author = Author(normalized_name="new author", display_name="New Author")
    series = Series(normalized_name="new series", display_name="New Series")
    tag = Tag(normalized_name="new tag", display_name="New Tag")
    case.item.authors = [author]
    case.item.series = series
    case.item.tags = [tag]
    session.commit()

    result = ConflictService(session=session, storage=storage).resolve(
        case.operation.id,
        ConflictResolution.REPLACE,
        idempotency_key="replace-once",
    )
    session.refresh(case.existing_asset)

    assert result.asset_id == case.existing_asset.id
    assert case.existing_asset.display_name == "Incoming display"
    assert case.existing_asset.sha256 == storage.sha256(case.existing_path)
    assert [value.id for value in case.existing_asset.authors] == [author.id]
    assert case.existing_asset.series_id == series.id
    assert [value.id for value in case.existing_asset.tags] == [tag.id]
    assert case.existing_path.read_bytes() == b"incoming-video"
    assert not case.source.exists()
    assert session.query(MediaAsset).count() == 1
    assert not (storage.backups_dir / case.operation.id).exists()


def test_changed_source_invalidates_old_resolution(
    session: Session,
    storage: LocalStorage,
) -> None:
    case = _conflict_case(session, storage)
    case.source.write_bytes(b"changed")

    with pytest.raises(ConflictChoiceExpired):
        ConflictService(session=session, storage=storage).resolve(
            case.operation.id,
            ConflictResolution.REPLACE,
            idempotency_key="expired-choice",
        )

    assert case.existing_path.read_bytes() == b"existing-video"
    assert case.source.read_bytes() == b"changed"
    assert session.query(MediaAsset).count() == 1


def test_unregistered_name_conflict_cannot_persist_replace_decision(
    session: Session,
    storage: LocalStorage,
) -> None:
    external_path = storage.library_dir / "external.mp4"
    external_path.write_bytes(b"unregistered-library-file")
    source = storage.inbox_dir / external_path.name
    source.write_bytes(b"incoming-video")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
        display_name="Incoming display",
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    session.add(item)
    session.commit()
    result = IngestService(session=session, storage=storage).ingest(
        item.id, f"external-{item.id}"
    )
    operation = session.get(IngestOperation, result.operation_id)
    assert operation is not None

    with pytest.raises(ConflictReplacementUnavailable):
        ConflictService(session=session, storage=storage).resolve(
            operation.id,
            ConflictResolution.REPLACE,
            idempotency_key="invalid-replace",
        )

    session.commit()
    session.refresh(operation)
    assert operation.resolution is None
    assert operation.resolution_idempotency_key is None
    assert item.status is InboxStatus.CONFLICT
    assert source.read_bytes() == b"incoming-video"
    assert external_path.read_bytes() == b"unregistered-library-file"
