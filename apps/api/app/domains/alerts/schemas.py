from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict


class AlertResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    source_domain: str
    source_record_id: int
    rule_code: str
    severity: str
    status: str
    title: str
    message: str
    explanation: str | None = None
    triggered_at: datetime
    viewed_at: datetime | None = None
    dismissed_at: datetime | None = None
    created_at: datetime


class AlertResultListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[AlertResultRead]
