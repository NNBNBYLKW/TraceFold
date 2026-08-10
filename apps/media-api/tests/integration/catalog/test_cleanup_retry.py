from __future__ import annotations

from pathlib import Path

import pytest
from sqlalchemy.orm import Session

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
    def __init__(self, delegate: StoragePort, fail_after: str | None) -> None:
        self.delegate = delegate
        self.fail_after = fail_after

    def __getattr__(self, name: str):
        return getattr(self.delegate, name)

    def delete_verified(self, path: Path, expected_sha256: str) -> None:
        if self.fail_after == "cleanup":
            raise SimulatedCrash("cleanup")
        self.delegate.delete_verified(path, expected_sha256)


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


def test_cleanup_failure_keeps_completed_asset_and_retries(
    session: Session,
    storage: LocalStorage,
) -> None:
    source = storage.inbox_dir / "cleanup.mp4"
    source.write_bytes(b"cleanup-video")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
    )
    item.sources.append(
        SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
    )
    session.add(item)
    session.commit()
    faulting = FaultingStorage(storage, "cleanup")

    first = IngestService(session=session, storage=faulting).ingest(
        item.id, "cleanup-retry"
    )
    operation = session.get(IngestOperation, first.operation_id)

    assert first.cleanup_warning == "CLEANUP_PENDING"
    assert operation is not None
    assert operation.state is IngestOperationState.CLEANUP_PENDING
    assert session.get(MediaAsset, operation.asset_id) is not None
    assert item.status is InboxStatus.COMPLETED
    assert source.exists()

    second = IngestRecoveryService(
        session=session,
        storage=faulting,
    ).retry_cleanup(operation.id)
    assert second.warning_code == "CLEANUP_PENDING"
    assert source.exists()

    faulting.fail_after = None
    third = IngestRecoveryService(
        session=session,
        storage=faulting,
    ).retry_cleanup(operation.id)
    session.refresh(operation)

    assert third.state == "completed"
    assert third.warning_code is None
    assert operation.state is IngestOperationState.COMPLETED
    assert not source.exists()
    assert session.get(MediaAsset, operation.asset_id) is not None

