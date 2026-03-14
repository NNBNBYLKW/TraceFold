from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


class PendingItemRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    capture_id: int
    parse_result_id: int
    target_domain: str
    status: str
    proposed_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    corrected_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    reason: str | None = None
    created_at: datetime
    resolved_at: datetime | None = None


class PendingConfirmRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    note: str | None = None


class PendingDiscardRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    note: str | None = None


class PendingFixRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    corrected_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    note: str | None = None


class PendingForceInsertRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    note: str | None = None


class PendingActionRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    pending_item_id: int
    action_type: str
    before_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    after_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    note: str | None = None
    created_at: datetime
