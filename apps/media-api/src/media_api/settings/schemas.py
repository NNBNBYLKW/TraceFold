from __future__ import annotations

from pathlib import Path
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class ProcessorSettingPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    processor_name: str = Field(min_length=1, max_length=150)
    enabled: bool
    auto_run: bool


class ProcessorSettingRead(BaseModel):
    processor_name: str
    enabled: bool
    auto_run: bool


class RuntimeSettingsPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    inbox_dir: Path | None = None
    library_dir: Path | None = None
    work_dir: Path | None = None
    scan_interval_seconds: int | None = Field(default=None, ge=2, le=3600)
    automatic_scan_enabled: bool | None = None
    processors: list[ProcessorSettingPatch] | None = None


class RuntimeSettingsRead(BaseModel):
    inbox_dir: Path
    library_dir: Path
    work_dir: Path
    database_path: Path
    scan_interval_seconds: int
    automatic_scan_enabled: bool
    processors: list[ProcessorSettingRead]


class SystemStatusRead(BaseModel):
    directories_writable: bool
    ffmpeg_available: bool
    ffprobe_available: bool
    last_scan_at: datetime | None
    queued_jobs: int
    recoverable_operations: int
    recovery_in_progress: bool
    ingest_enabled: bool
