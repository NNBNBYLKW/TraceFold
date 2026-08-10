from __future__ import annotations

import os
import shutil
from collections.abc import Collection
from pathlib import Path

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from media_api.catalog.models import IngestOperation, IngestOperationState
from media_api.infrastructure.config import BootstrapSettings
from media_api.processing.models import ProcessingJob, ProcessingJobStatus
from media_api.settings.models import AppSetting, ProcessorSetting
from media_api.settings.schemas import (
    ProcessorSettingRead,
    RuntimeSettingsPatch,
    RuntimeSettingsRead,
    SystemStatusRead,
)
from media_api.storage.local import LocalStorage


class SettingsError(RuntimeError):
    pass


class SettingsBusy(SettingsError):
    pass


class SettingsProcessorNotFound(SettingsError):
    pass


PATH_KEYS = ("inbox_dir", "library_dir", "work_dir")
OVERRIDE_KEYS = (*PATH_KEYS, "scan_interval_seconds", "automatic_scan_enabled")
ACTIVE_INGEST_STATES = (
    IngestOperationState.PREPARED,
    IngestOperationState.FILE_COMMITTED,
    IngestOperationState.DB_COMMITTED,
    IngestOperationState.CLEANUP_PENDING,
)


class SettingsService:
    def __init__(
        self,
        *,
        session: Session,
        bootstrap: BootstrapSettings,
        storage: LocalStorage,
        runtime_state: object | None = None,
        processor_names: Collection[str] = (),
    ) -> None:
        self.session = session
        self.bootstrap = bootstrap
        self.storage = storage
        self.runtime_state = runtime_state
        self.processor_names = frozenset(processor_names)

    def get(self) -> RuntimeSettingsRead:
        overrides = {
            setting.key: setting.value_json
            for setting in self.session.scalars(
                select(AppSetting).where(AppSetting.key.in_(OVERRIDE_KEYS))
            )
        }
        return RuntimeSettingsRead(
            inbox_dir=Path(overrides.get("inbox_dir", self.bootstrap.inbox_dir)).resolve(),
            library_dir=Path(
                overrides.get("library_dir", self.bootstrap.library_dir)
            ).resolve(),
            work_dir=Path(overrides.get("work_dir", self.bootstrap.work_dir)).resolve(),
            database_path=self.bootstrap.database_path.resolve(),
            scan_interval_seconds=int(
                overrides.get(
                    "scan_interval_seconds",
                    self.bootstrap.scan_interval_seconds,
                )
            ),
            automatic_scan_enabled=bool(
                overrides.get("automatic_scan_enabled", False)
            ),
            processors=self._processor_settings(),
        )

    def _processor_settings(self) -> list[ProcessorSettingRead]:
        persisted = {
            setting.processor_name: setting
            for setting in self.session.scalars(select(ProcessorSetting))
        }
        return [
            ProcessorSettingRead(
                processor_name=name,
                enabled=persisted[name].enabled if name in persisted else True,
                auto_run=(
                    persisted[name].auto_run_default if name in persisted else True
                ),
            )
            for name in sorted(self.processor_names | persisted.keys())
        ]

    def update(self, patch: RuntimeSettingsPatch) -> RuntimeSettingsRead:
        if patch.processors is not None and self.processor_names:
            unknown_processors = [
                processor.processor_name
                for processor in patch.processors
                if processor.processor_name not in self.processor_names
            ]
            if unknown_processors:
                raise SettingsProcessorNotFound(unknown_processors[0])
        current = self.get()
        patch_values = patch.model_dump(exclude_none=True, exclude={"processors"})
        paths_changed = any(
            key in patch_values
            and Path(patch_values[key]).resolve() != getattr(current, key)
            for key in PATH_KEYS
        )
        if paths_changed and self._has_active_work():
            raise SettingsBusy("runtime paths cannot change while work is active")

        effective = current.model_copy(update=patch_values)
        validated = BootstrapSettings.model_validate(
            {
                **self.bootstrap.model_dump(),
                "inbox_dir": effective.inbox_dir,
                "library_dir": effective.library_dir,
                "work_dir": effective.work_dir,
                "database_path": current.database_path,
                "scan_interval_seconds": effective.scan_interval_seconds,
            }
        )
        if paths_changed:
            self.storage.reconfigure(validated)

        values = {
            "inbox_dir": str(validated.inbox_dir.resolve()),
            "library_dir": str(validated.library_dir.resolve()),
            "work_dir": str(validated.work_dir.resolve()),
            "scan_interval_seconds": validated.scan_interval_seconds,
            "automatic_scan_enabled": effective.automatic_scan_enabled,
        }
        for key, value in values.items():
            default = self._default_value(key)
            setting = self.session.get(AppSetting, key)
            if value == default:
                if setting is not None:
                    self.session.delete(setting)
                continue
            if setting is None:
                setting = AppSetting(key=key, value_json=value)
                self.session.add(setting)
            else:
                setting.value_json = value

        if patch.processors is not None:
            for processor_patch in patch.processors:
                setting = self.session.get(
                    ProcessorSetting,
                    processor_patch.processor_name,
                )
                if setting is None:
                    setting = ProcessorSetting(
                        processor_name=processor_patch.processor_name,
                    )
                    self.session.add(setting)
                setting.enabled = processor_patch.enabled
                setting.auto_run_default = processor_patch.auto_run
        self.session.commit()
        return self.get()

    def system_status(self) -> SystemStatusRead:
        current = self.get()
        queued_jobs = self.session.scalar(
            select(func.count()).select_from(ProcessingJob).where(
                ProcessingJob.status == ProcessingJobStatus.QUEUED
            )
        ) or 0
        recoverable_operations = self.session.scalar(
            select(func.count()).select_from(IngestOperation).where(
                IngestOperation.state.in_(ACTIVE_INGEST_STATES),
                IngestOperation.conflict_kinds_json.is_(None),
            )
        ) or 0
        state = self.runtime_state
        return SystemStatusRead(
            directories_writable=all(
                _directory_writable(path)
                for path in (current.inbox_dir, current.library_dir, current.work_dir)
            ),
            ffmpeg_available=shutil.which("ffmpeg") is not None,
            ffprobe_available=shutil.which("ffprobe") is not None,
            last_scan_at=(
                getattr(state, "last_scan_at", None) if state is not None else None
            ),
            queued_jobs=int(queued_jobs),
            recoverable_operations=int(recoverable_operations),
            recovery_in_progress=bool(
                getattr(state, "recovery_in_progress", False)
            ),
            ingest_enabled=bool(getattr(state, "ingest_enabled", True)),
        )

    def _has_active_work(self) -> bool:
        active_operation = self.session.scalar(
            select(IngestOperation.id).where(
                IngestOperation.state.in_(ACTIVE_INGEST_STATES)
            ).limit(1)
        )
        running_job = self.session.scalar(
            select(ProcessingJob.id).where(
                ProcessingJob.status == ProcessingJobStatus.RUNNING
            ).limit(1)
        )
        return active_operation is not None or running_job is not None

    def _default_value(self, key: str) -> object:
        if key in PATH_KEYS:
            return str(Path(getattr(self.bootstrap, key)).resolve())
        if key == "automatic_scan_enabled":
            return False
        return getattr(self.bootstrap, key)


def _directory_writable(path: Path) -> bool:
    return path.is_dir() and os.access(path, os.W_OK)
