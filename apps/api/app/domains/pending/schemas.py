from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict

from app.schemas.common import PaginatedListRead


class PendingListItemRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    status: str
    target_domain: str
    reason_preview: str | None = None
    created_at: datetime
    has_corrected_payload: bool
    source_capture_id: int
    is_next_to_review: bool


class PendingListRead(PaginatedListRead):
    model_config = ConfigDict(extra="forbid")

    items: list[PendingListItemRead]
    next_pending_item_id: int | None = None


class PendingDetailRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    status: str
    target_domain: str
    reason: str | None = None
    proposed_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    corrected_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    created_at: datetime
    resolved_at: datetime | None = None
    source_capture_id: int
    parse_result_id: int


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
