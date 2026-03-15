from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict

from app.schemas.common import PaginatedListRead


class KnowledgeListItemRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    created_at: datetime
    display_title: str
    content_preview: str | None = None
    has_source_text: bool
    has_source_pending: bool


class KnowledgeListRead(PaginatedListRead):
    model_config = ConfigDict(extra="forbid")

    items: list[KnowledgeListItemRead]


class KnowledgeDetailRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: int
    created_at: datetime
    title: str
    content: str | None = None
    source_text: str | None = None
    source_capture_id: int
    source_pending_id: int | None = None
