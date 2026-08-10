from __future__ import annotations

from pathlib import Path

import pytest
from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState, MediaAsset
from media_api.catalog.recovery import (
    IngestRecoveryService,
    RecoveryRequiresAttention,
)
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

    def commit_stage(self, staged: Path, relative_target: Path) -> Path:
        result = self.delegate.commit_stage(staged, relative_target)
        if self.fail_after == "file_commit":
            raise SimulatedCrash("file_commit")
        return result


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


def _ready_item(session: Session, storage: LocalStorage) -> tuple[InboxItem, Path]:
    source = storage.inbox_dir / "recover.mp4"
    source.write_bytes(b"recoverable-video")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
        display_name="Recover me",
    )
    item.sources.append(
        SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
    )
    session.add(item)
    session.commit()
    return item, source


def _operation_at_checkpoint(
    checkpoint: IngestOperationState,
    session: Session,
    storage: LocalStorage,
) -> tuple[IngestOperation, Path]:
    item, source = _ready_item(session, storage)
    ingest = IngestService(session=session, storage=storage)
    operation = ingest.prepare(item.id, f"recover-{checkpoint.value}")

    if checkpoint is IngestOperationState.PREPARED:
        return operation, source

    final = storage.commit_stage(
        Path(operation.staged_path or ""),
        Path(operation.requested_actual_filename),
    )
    operation.final_relative_path = final.name
    operation.state = IngestOperationState.FILE_COMMITTED
    item.status = InboxStatus.INGESTING
    session.commit()
    if checkpoint is IngestOperationState.FILE_COMMITTED:
        return operation, source

    recovery = IngestRecoveryService(session=session, storage=storage)
    recovery.finish_database_commit(operation.id)
    operation.state = checkpoint
    session.commit()
    return operation, source


@pytest.mark.parametrize(
    "checkpoint",
    [
        IngestOperationState.PREPARED,
        IngestOperationState.FILE_COMMITTED,
        IngestOperationState.DB_COMMITTED,
        IngestOperationState.CLEANUP_PENDING,
    ],
)
def test_restart_recovers_without_loss_or_duplicate(
    checkpoint: IngestOperationState,
    session: Session,
    storage: LocalStorage,
) -> None:
    operation, source = _operation_at_checkpoint(checkpoint, session, storage)
    if checkpoint in {
        IngestOperationState.PREPARED,
        IngestOperationState.FILE_COMMITTED,
    }:
        assert source.exists()
        assert session.scalar(select(MediaAsset)) is None

    results = IngestRecoveryService(session=session, storage=storage).recover_all()
    session.refresh(operation)
    assets = list(session.scalars(select(MediaAsset)))

    assert [result.operation_id for result in results] == [operation.id]
    assert operation.state is IngestOperationState.COMPLETED
    assert len(assets) == 1
    assert not source.exists()
    final = storage.library_dir / operation.requested_actual_filename
    assert final.exists()
    assert storage.sha256(final) == operation.source_hash

    IngestRecoveryService(session=session, storage=storage).recover_all()
    assert len(list(session.scalars(select(MediaAsset)))) == 1


def test_recovers_crash_after_file_move_before_checkpoint_is_saved(
    session: Session,
    storage: LocalStorage,
) -> None:
    item, source = _ready_item(session, storage)
    operation = IngestService(session=session, storage=storage).prepare(
        item.id, "move-before-checkpoint"
    )
    with pytest.raises(SimulatedCrash, match="file_commit"):
        IngestService(
            session=session,
            storage=FaultingStorage(storage, "file_commit"),
        ).ingest(item.id, "move-before-checkpoint")
    final = storage.library_dir / operation.requested_actual_filename
    assert operation.state is IngestOperationState.PREPARED
    assert not Path(operation.staged_path or "").exists()

    result = IngestRecoveryService(session=session, storage=storage).recover(operation.id)

    assert result.state == "completed"
    assert final.exists()
    assert not source.exists()
    assert session.scalar(select(MediaAsset)) is not None


def test_unknown_final_file_is_never_overwritten(
    session: Session,
    storage: LocalStorage,
) -> None:
    item, source = _ready_item(session, storage)
    operation = IngestService(session=session, storage=storage).prepare(
        item.id, "unexpected-final"
    )
    staged = Path(operation.staged_path or "")
    unexpected_final = storage.library_dir / operation.requested_actual_filename
    unexpected_final.write_bytes(b"unrelated-existing-file")

    with pytest.raises(RecoveryRequiresAttention, match="hash"):
        IngestRecoveryService(session=session, storage=storage).recover(operation.id)

    assert source.read_bytes() == b"recoverable-video"
    assert staged.read_bytes() == b"recoverable-video"
    assert unexpected_final.read_bytes() == b"unrelated-existing-file"
    assert session.scalar(select(MediaAsset)) is None


def test_missing_source_before_database_commit_requires_attention(
    session: Session,
    storage: LocalStorage,
) -> None:
    operation, source = _operation_at_checkpoint(
        IngestOperationState.FILE_COMMITTED, session, storage
    )
    source.unlink()

    with pytest.raises(RecoveryRequiresAttention, match="source"):
        IngestRecoveryService(session=session, storage=storage).recover(operation.id)

    assert session.scalar(select(MediaAsset)) is None
    assert (storage.library_dir / operation.requested_actual_filename).exists()
