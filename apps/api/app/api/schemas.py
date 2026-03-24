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
