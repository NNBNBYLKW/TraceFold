from __future__ import annotations

from pydantic import BaseModel

from media_api.taxonomy.models import Author, Series, Tag


class TaxonomyNameWrite(BaseModel):
    name: str


class TaxonomyRead(BaseModel):
    id: str
    display_name: str

    @classmethod
    def from_model(cls, entry: Author | Series | Tag) -> TaxonomyRead:
        return cls(id=entry.id, display_name=entry.display_name)
