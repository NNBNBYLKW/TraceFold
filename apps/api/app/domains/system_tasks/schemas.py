from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


class TaskRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    task_type: str
    status: str
    payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    result_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    error_message: str | None = None
    created_at: datetime
    started_at: datetime | None = None
    finished_at: datetime | None = None


class TaskCreateResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    task_id: int
