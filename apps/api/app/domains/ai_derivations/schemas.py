from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class AiDerivationRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    target_type: str
    target_id: int
    derivation_kind: str
    status: str
    model_key: str | None = None
    model_version: str | None = None
    source_basis_json: JsonValue = None
    content_json: JsonValue = None
    error_message: str | None = None
    generated_at: datetime | None = None
    invalidated_at: datetime | None = None
    created_at: datetime
    updated_at: datetime

    # Compatibility fields retained while older callers/tests move to the
    # formal derivation naming.
    target_domain: str
    target_record_id: int
    derivation_type: str
    model_name: str | None = None
    failed_at: datetime | None = None


class AiDerivationListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[AiDerivationRead]


class AiDerivationTaskSubmissionRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    task_id: int
    task_type: str
    target_type: str
    target_id: int
    derivation_kind: str
    derivation_status: str


class AiDerivationInvalidateRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    target_type: str
    target_id: int
    derivation_kind: str
    status: str
    invalidated_at: datetime


AiDerivationResultRead = AiDerivationRead
AiDerivationResultListRead = AiDerivationListRead
