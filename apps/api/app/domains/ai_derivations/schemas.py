from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class AiDerivationResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    target_domain: str
    target_record_id: int
    derivation_type: str
    status: str
    model_name: str | None = None
    model_version: str | None = None
    generated_at: datetime | None = None
    failed_at: datetime | None = None
    content_json: JsonValue = None
    error_message: str | None = None
    created_at: datetime


class AiDerivationResultListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[AiDerivationResultRead]
