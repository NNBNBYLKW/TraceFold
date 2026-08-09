from __future__ import annotations

from dataclasses import dataclass
from typing import Generic, TypeVar

from sqlalchemy.orm import Session

from media_api.taxonomy.models import Author, Series, Tag
from media_api.taxonomy.repository import TaxonomyRepository


T = TypeVar("T", Author, Series, Tag)


@dataclass(frozen=True)
class NormalizedName:
    display: str
    normalized: str


class TaxonomyError(ValueError):
    pass


class EmptyTaxonomyName(TaxonomyError):
    pass


class TaxonomyNameConflict(TaxonomyError):
    pass


class TaxonomyNotFound(TaxonomyError):
    pass


def normalize_taxonomy_name(raw: str) -> NormalizedName:
    display = " ".join(raw.strip().split())
    if not display:
        raise EmptyTaxonomyName("taxonomy name must not be empty")
    return NormalizedName(display=display, normalized=display.casefold())


class TaxonomyService(Generic[T]):
    model: type[T]

    def __init__(self, session: Session) -> None:
        self.session = session
        self.repository = TaxonomyRepository(session, self.model)

    def list(self) -> list[T]:
        return self.repository.list()

    def create(self, raw_name: str) -> T:
        name = normalize_taxonomy_name(raw_name)
        existing = self.repository.by_normalized_name(name.normalized)
        if existing is not None:
            return existing

        entry = self.model(
            normalized_name=name.normalized,
            display_name=name.display,
        )
        self.session.add(entry)
        self.session.commit()
        self.session.refresh(entry)
        return entry

    def update(self, entry_id: str, raw_name: str) -> T:
        entry = self.repository.get(entry_id)
        if entry is None:
            raise TaxonomyNotFound(entry_id)

        name = normalize_taxonomy_name(raw_name)
        existing = self.repository.by_normalized_name(name.normalized)
        if existing is not None and existing.id != entry.id:
            raise TaxonomyNameConflict(name.display)

        entry.display_name = name.display
        entry.normalized_name = name.normalized
        self.session.commit()
        self.session.refresh(entry)
        return entry


class AuthorService(TaxonomyService[Author]):
    model = Author


class SeriesService(TaxonomyService[Series]):
    model = Series


class TagService(TaxonomyService[Tag]):
    model = Tag
