from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


class AlertRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    domain: str
    rule_key: str
    severity: str
    status: str
    source_record_type: str
    source_record_id: int
    title: str | None = None
    message: str
    details_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    source_domain: str | None = None
    rule_code: str | None = None
    explanation: str | None = None
    triggered_at: datetime
    acknowledged_at: datetime | None = None
    resolved_at: datetime | None = None
    viewed_at: datetime | None = None
    dismissed_at: datetime | None = None
    resolution_note: str | None = None
    created_at: datetime
    updated_at: datetime


class AlertListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[AlertRead]
    limit: int
    total: int


class AlertResolveRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    resolution_note: str | None = None


AlertResultRead = AlertRead
AlertResultListRead = AlertListRead
