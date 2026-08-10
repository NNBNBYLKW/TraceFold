from __future__ import annotations

import asyncio

import pytest
from fastapi.testclient import TestClient

from media_api.api.dependencies import RuntimeState
from media_api.api.events import EventBus
from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.infrastructure.lifecycle import RuntimeCoordinator, RuntimeLoopSettings
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.main import create_app
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage


@pytest.mark.asyncio
async def test_recovery_finishes_before_ingest_is_enabled() -> None:
    calls: list[str] = []
    state = RuntimeState()

    def recover_ingests() -> None:
        calls.append("recover_ingests")
        assert state.ingest_enabled is False

    def recover_processing_jobs() -> None:
        calls.append("recover_processing_jobs")
        assert state.ingest_enabled is False

    coordinator = RuntimeCoordinator(
        state=state,
        event_bus=EventBus(),
        recover_ingests=recover_ingests,
        recover_processing_jobs=recover_processing_jobs,
        run_worker_once=lambda: None,
        scan_once=lambda: calls.append("scan"),
        load_loop_settings=lambda: RuntimeLoopSettings(
            automatic_scan_enabled=False,
            scan_interval_seconds=10,
        ),
        dispose=lambda: calls.append("dispose"),
        idle_poll_seconds=0.01,
    )

    await coordinator.start()

    assert coordinator.lifecycle_events[:3] == [
        "disable_ingest",
        "recover_ingests",
        "recover_processing_jobs",
    ]
    assert calls[:2] == ["recover_ingests", "recover_processing_jobs"]
    assert state.ingest_enabled is True
    assert state.recovery_in_progress is False

    await coordinator.stop()
    assert calls[-1] == "dispose"


@pytest.mark.asyncio
async def test_automatic_scan_loop_runs_only_when_enabled() -> None:
    scans = 0

    def scan_once() -> None:
        nonlocal scans
        scans += 1

    coordinator = RuntimeCoordinator(
        state=RuntimeState(),
        event_bus=EventBus(),
        recover_ingests=lambda: None,
        recover_processing_jobs=lambda: None,
        run_worker_once=lambda: None,
        scan_once=scan_once,
        load_loop_settings=lambda: RuntimeLoopSettings(
            automatic_scan_enabled=True,
            scan_interval_seconds=0.01,
        ),
        dispose=lambda: None,
        idle_poll_seconds=0.01,
    )

    await coordinator.start()
    for _ in range(100):
        if scans:
            break
        await asyncio.sleep(0.001)
    await coordinator.stop()

    assert scans >= 1


def test_application_startup_recovers_prepared_ingest_before_serving(settings) -> None:
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    storage = LocalStorage(settings)
    storage.provision_directories(
        RuntimePaths(
            inbox_dir=settings.inbox_dir,
            library_dir=settings.library_dir,
            work_dir=settings.work_dir,
        )
    )
    source = storage.inbox_dir / "recover-on-start.jpg"
    source.write_bytes(b"image")
    with session_factory() as session:
        item = InboxItem(
            media_type="image",
            status=InboxStatus.READY_TO_TAG,
            suggested_actual_filename=source.name,
        )
        item.sources.append(
            SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
        )
        session.add(item)
        session.commit()
        operation = IngestService(session=session, storage=storage).prepare(
            item.id,
            "recover-on-start",
        )
        operation_id = operation.id

    application = create_app(settings)
    with TestClient(application):
        assert application.state.runtime_state.ingest_enabled is True
        with application.state.session_factory() as session:
            recovered = session.get(IngestOperation, operation_id)
            assert recovered is not None
            assert recovered.state is IngestOperationState.COMPLETED
    assert not source.exists()
    assert (storage.library_dir / source.name).exists()

    Base.metadata.drop_all(engine)
    engine.dispose()
