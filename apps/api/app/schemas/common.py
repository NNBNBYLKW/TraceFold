from __future__ import annotations

from pydantic import BaseModel, ConfigDict


class PaginatedListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    page: int
    page_size: int
    total: int
