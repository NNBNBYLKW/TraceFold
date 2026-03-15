from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict

from app.schemas.common import PaginatedListRead


class ExpenseListItemRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    created_at: datetime
    amount: str
    currency: str
    category: str | None = None
    note_preview: str | None = None
    has_source_pending: bool


class ExpenseListRead(PaginatedListRead):
    model_config = ConfigDict(extra="forbid")

    items: list[ExpenseListItemRead]


class ExpenseDetailRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    created_at: datetime
    amount: str
    currency: str
    category: str | None = None
    note: str | None = None
    source_capture_id: int
    source_pending_id: int | None = None
