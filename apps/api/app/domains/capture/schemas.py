from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


class CaptureSubmitRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    source_type: str
    source_ref: str | None = None
    raw_text: str | None = None
    raw_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None


class CaptureRecordRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    source_type: str
    source_ref: str | None = None
    raw_text: str | None = None
    raw_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    status: str
    created_at: datetime
    finalized_at: datetime | None = None


class ParseResultRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    capture_id: int
    target_domain: str
    confidence_score: float
    confidence_level: str
    parsed_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    parser_name: str
    parser_version: str
    created_at: datetime
