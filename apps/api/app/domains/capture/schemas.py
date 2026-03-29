from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from app.schemas.common import PaginatedListRead


class CaptureSubmitRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    raw_text: str
    source_type: str = Field(default="manual")
    source_ref: str | None = None


class CaptureSubmitResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    capture_created: bool
    capture_id: int
    status: str
    route: str
    target_domain: str
    pending_item_id: int | None = None
    formal_record_id: int | None = None


class BulkCapturePreviewRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    file_name: str
    text_content: str


class BulkCapturePreviewCandidateRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    index: int
    raw_text: str
    preview: str | None = None
    char_count: int
    is_valid: bool
    issue: str | None = None


class BulkCapturePreviewRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    file_name: str
    split_strategy: str
    candidate_count: int
    valid_count: int
    invalid_count: int
    candidates: list[BulkCapturePreviewCandidateRead]


class BulkCaptureImportRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    file_name: str
    entries: list[str]


class BulkCaptureImportResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    file_name: str
    imported_count: int
    skipped_count: int
    pending_count: int
    committed_count: int
    capture_ids: list[int]


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


class CaptureListItemRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    status: str
    source_type: str
    source_ref: str | None = None
    summary: str | None = None
    target_domain: str | None = None
    current_stage: str
    pending_item_id: int | None = None
    formal_record_id: int | None = None
    formal_source_pending_id: int | None = None
    created_at: datetime
    updated_at: datetime


class CaptureListRead(PaginatedListRead):
    model_config = ConfigDict(extra="forbid")

    items: list[CaptureListItemRead]


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


class CapturePendingLinkRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    status: str
    target_domain: str
    summary: str | None = None
    actionable: bool
    resolved_at: datetime | None = None


class CaptureFormalResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    target_domain: str
    record_id: int
    summary: str | None = None
    source_pending_id: int | None = None
    created_at: datetime


class CaptureDetailRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    status: str
    source_type: str
    source_ref: str | None = None
    summary: str | None = None
    target_domain: str | None = None
    current_stage: str
    chain_summary: str
    raw_text: str | None = None
    raw_payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None
    created_at: datetime
    updated_at: datetime
    finalized_at: datetime | None = None
    parse_result: ParseResultRead | None = None
    pending_item: CapturePendingLinkRead | None = None
    formal_result: CaptureFormalResultRead | None = None
