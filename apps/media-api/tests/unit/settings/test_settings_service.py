from __future__ import annotations

from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.catalog.models import IngestOperation, IngestOperationState
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxItem
from media_api.intake.state import InboxStatus
from media_api.settings.schemas import ProcessorSettingPatch, RuntimeSettingsPatch
from media_api.settings.service import (
    SettingsBusy,
    SettingsProcessorNotFound,
    SettingsService,
)
from media_api.storage.local import LocalStorage


@pytest.fixture
def session(settings) -> Session:
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


def test_rejects_path_change_while_operation_active(
    session: Session,
    settings,
    storage: LocalStorage,
    tmp_path: Path,
) -> None:
    item = InboxItem(
        media_type="video",
        status=InboxStatus.CONFLICT,
        suggested_actual_filename="active.mp4",
    )
    operation = IngestOperation(
        inbox_item=item,
        idempotency_key="active-operation",
        state=IngestOperationState.PREPARED,
        requested_actual_filename="active.mp4",
    )
    session.add(operation)
    session.commit()
    other_inbox = tmp_path / "other-inbox"

    with pytest.raises(SettingsBusy):
        SettingsService(
            session=session,
            bootstrap=settings,
            storage=storage,
        ).update(RuntimeSettingsPatch(inbox_dir=other_inbox))

    assert not other_inbox.exists()
    assert storage.inbox_dir == settings.inbox_dir.resolve()


def test_applies_valid_paths_and_creates_directories(
    session: Session,
    settings,
    storage: LocalStorage,
    tmp_path: Path,
) -> None:
    service = SettingsService(
        session=session,
        bootstrap=settings,
        storage=storage,
    )

    updated = service.update(
        RuntimeSettingsPatch(
            inbox_dir=tmp_path / "next-inbox",
            library_dir=tmp_path / "next-library",
            work_dir=tmp_path / "next-work",
            scan_interval_seconds=25,
            automatic_scan_enabled=True,
        )
    )

    assert updated.inbox_dir.exists()
    assert updated.library_dir.exists()
    assert updated.work_dir.exists()
    assert updated.database_path == settings.database_path.resolve()
    assert updated.scan_interval_seconds == 25
    assert updated.automatic_scan_enabled is True
    assert storage.inbox_dir == updated.inbox_dir
    assert service.get() == updated


def test_non_path_setting_can_change_during_active_operation(
    session: Session,
    settings,
    storage: LocalStorage,
) -> None:
    item = InboxItem(
        media_type="image",
        status=InboxStatus.CONFLICT,
        suggested_actual_filename="active.jpg",
    )
    session.add(
        IngestOperation(
            inbox_item=item,
            idempotency_key="active-non-path",
            state=IngestOperationState.FILE_COMMITTED,
            requested_actual_filename="active.jpg",
        )
    )
    session.commit()

    updated = SettingsService(
        session=session,
        bootstrap=settings,
        storage=storage,
    ).update(RuntimeSettingsPatch(scan_interval_seconds=30))

    assert updated.scan_interval_seconds == 30


def test_invalid_processor_does_not_partially_apply_path_change(
    session: Session,
    settings,
    storage: LocalStorage,
    tmp_path: Path,
) -> None:
    next_inbox = tmp_path / "not-applied-inbox"
    service = SettingsService(
        session=session,
        bootstrap=settings,
        storage=storage,
        processor_names={"registered"},
    )

    with pytest.raises(SettingsProcessorNotFound):
        service.update(
            RuntimeSettingsPatch(
                inbox_dir=next_inbox,
                processors=[
                    ProcessorSettingPatch(
                        processor_name="unknown",
                        enabled=False,
                        auto_run=False,
                    )
                ],
            )
        )

    assert storage.inbox_dir == settings.inbox_dir.resolve()
    assert not next_inbox.exists()
