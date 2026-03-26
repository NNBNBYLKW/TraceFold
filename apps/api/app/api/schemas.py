from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict


class PingRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    message: str


class HealthzRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: str


class RuntimeStatusRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    api_status: str
    db_status: str
    migration_head: str | None = None
    schema_version: str | None = None
    migration_status: str
    degraded_reasons: list[str]
    task_runtime_status: str
    last_checked_at: datetime


class LocalOperabilityRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    database_path: str
    database_exists: bool
    backup_directory: str
    transfer_directory: str
    daily_use_readiness: str
    readiness_message: str
    warnings: list[str]
    guidance: list[str]
    backup_scope: str
    restore_scope: str
    export_scope: str
    import_scope: str


class LocalBackupRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    destination_path: str | None = None


class LocalBackupRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    backup_created: bool
    database_path: str
    backup_path: str
    file_size_bytes: int
    created_at: datetime


class LocalRestoreRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    source_path: str
    create_safety_backup: bool = True


class LocalRestoreRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    restore_completed: bool
    source_path: str
    database_path: str
    safety_backup_path: str | None = None
    restored_at: datetime


class CaptureBundleExportRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    destination_path: str | None = None


class CaptureBundleExportRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    export_created: bool
    export_path: str
    item_count: int
    skipped_count: int
    created_at: datetime


class CaptureBundleImportRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    source_path: str


class CaptureBundleImportRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    import_completed: bool
    source_path: str
    imported_count: int
    pending_count: int
    committed_count: int
    imported_at: datetime
