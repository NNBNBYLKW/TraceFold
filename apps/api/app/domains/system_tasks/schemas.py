from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


class TaskRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    task_type: str
    status: str
    trigger_source: str
    payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    result_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    error_message: str | None = None
    attempt_count: int
    requested_at: datetime
    started_at: datetime | None = None
    finished_at: datetime | None = None
    idempotency_key: str | None = None
    created_at: datetime
    updated_at: datetime


class TaskListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[TaskRead]
    limit: int
    total: int


class TaskCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None


class TaskCreateResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    task_id: int
    task_type: str
    status: str


class TaskCancelResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    task_id: int
    status: str
