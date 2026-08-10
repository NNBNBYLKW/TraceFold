from __future__ import annotations

from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.catalog.conflicts import ConflictResolution, ConflictService
from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState, MediaAsset
from media_api.catalog.recovery import IngestRecoveryService
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.contracts import StoragePort
from media_api.storage.local import LocalStorage


class SimulatedCrash(RuntimeError):
    pass


class FaultingStorage:
    def __init__(self, delegate: StoragePort, fail_after: str) -> None:
        self.delegate = delegate
        self.fail_after = fail_after

    def __getattr__(self, name: str):
        return getattr(self.delegate, name)

    def backup_existing(self, operation_id: str, target: Path) -> Path:
        backup = self.delegate.backup_existing(operation_id, target)
        if self.fail_after == "backup":
            raise SimulatedCrash("backup")
        return backup

    def commit_stage(self, staged: Path, relative_target: Path) -> Path:
        final = self.delegate.commit_stage(staged, relative_target)
        if self.fail_after == "file_commit":
            raise SimulatedCrash("file_commit")
        return final


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


def _replacement_conflict(
    session: Session,
    storage: LocalStorage,
) -> tuple[IngestOperation, InboxItem, MediaAsset, Path, Path]:
    final = storage.library_dir / "replace.mp4"
    final.write_bytes(b"old-formal")
    asset = MediaAsset(
        media_type="video",
        actual_filename=final.name,
        final_relative_path=final.name,
        display_name="Old metadata",
        size=final.stat().st_size,
        sha256=storage.sha256(final),
    )
    source = storage.inbox_dir / final.name
    source.write_bytes(b"new-incoming")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
        display_name="New metadata",
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    session.add_all([asset, item])
    session.commit()
    conflict = IngestService(session=session, storage=storage).ingest(
        item.id, f"replace-{item.id}"
    )
    operation = session.get(IngestOperation, conflict.operation_id)
    assert operation is not None
    return operation, item, asset, source, final


def test_recovery_restores_old_file_and_metadata_when_staged_input_is_lost(
    session: Session,
    storage: LocalStorage,
) -> None:
    operation, item, asset, source, final = _replacement_conflict(session, storage)
    with pytest.raises(SimulatedCrash, match="backup"):
        ConflictService(
            session=session,
            storage=FaultingStorage(storage, "backup"),
        ).resolve(
            operation.id,
            ConflictResolution.REPLACE,
            idempotency_key="replace-backup-crash",
        )
    session.refresh(operation)
    assert operation.staged_path is not None
    Path(operation.staged_path).unlink()

    result = IngestRecoveryService(session=session, storage=storage).recover(operation.id)
    session.refresh(operation)
    session.refresh(asset)

    assert result.state == "failed"
    assert operation.state is IngestOperationState.FAILED
    assert item.status is InboxStatus.READY_TO_TAG
    assert asset.display_name == "Old metadata"
    assert asset.sha256 == storage.sha256(final)
    assert final.read_bytes() == b"old-formal"
    assert source.read_bytes() == b"new-incoming"
    assert not (storage.backups_dir / operation.id).exists()


def test_recovery_finishes_replace_after_new_file_move(
    session: Session,
    storage: LocalStorage,
) -> None:
    operation, item, asset, source, final = _replacement_conflict(session, storage)
    with pytest.raises(SimulatedCrash, match="file_commit"):
        ConflictService(
            session=session,
            storage=FaultingStorage(storage, "file_commit"),
        ).resolve(
            operation.id,
            ConflictResolution.REPLACE,
            idempotency_key="replace-file-crash",
        )
    session.refresh(operation)
    assert operation.state is IngestOperationState.PREPARED
    assert final.read_bytes() == b"new-incoming"

    result = IngestRecoveryService(session=session, storage=storage).recover(operation.id)
    session.refresh(asset)

    assert result.state == "completed"
    assert asset.id == operation.replacement_asset_id
    assert asset.display_name == "New metadata"
    assert final.read_bytes() == b"new-incoming"
    assert not source.exists()
    assert session.query(MediaAsset).count() == 1
    assert not (storage.backups_dir / operation.id).exists()
