from __future__ import annotations

from typing import Generic, TypeVar

from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.taxonomy.models import Author, Series, Tag


TaxonomyModel = Author | Series | Tag
TaxonomyModelType = type[Author] | type[Series] | type[Tag]
T = TypeVar("T", Author, Series, Tag)


class TaxonomyRepository(Generic[T]):
    def __init__(self, session: Session, model: type[T]) -> None:
        self.session = session
        self.model = model

    def list(self) -> list[T]:
        return list(
            self.session.scalars(
                select(self.model).order_by(
                    self.model.normalized_name,
                    self.model.id,
                )
            )
        )

    def get(self, entry_id: str) -> T | None:
        return self.session.get(self.model, entry_id)

    def by_normalized_name(self, normalized_name: str) -> T | None:
        return self.session.scalar(
            select(self.model).where(
                self.model.normalized_name == normalized_name
            )
        )
