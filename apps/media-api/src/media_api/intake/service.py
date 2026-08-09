from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta

from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.infrastructure.db import utc_now
from media_api.intake.models import (
    InboxItem,
    InboxStatusEvent,
    ScanCandidate,
    SourceFile,
)
from media_api.intake.repository import InboxRepository
from media_api.intake.scanner import InboxScanner
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage
from media_api.taxonomy.models import Author, Series, Tag


@dataclass(frozen=True)
class ScanResult:
    observed_candidates: int
    stable_candidates: int
    created_items: int


class InboxCommandError(ValueError):
    pass


class InboxItemNotFound(InboxCommandError):
    pass


class InboxActionNotAllowed(InboxCommandError):
    pass


class TaxonomySelectionNotFound(InboxCommandError):
    pass


class InboxService:
    def __init__(self, session: Session) -> None:
        self.session = session
        self.items = InboxRepository(session)

    def list_items(self, *, include_completed: bool = False) -> list[InboxItem]:
        return self.items.list_items(include_completed=include_completed)

    def get_item(self, item_id: str) -> InboxItem | None:
        return self.items.get(item_id)


class InboxMetadataService(InboxService):
    def update_metadata(
        self,
        item_id: str,
        *,
        display_name: str | None,
        author_ids: list[str],
        series_id: str | None,
        tag_ids: list[str],
    ) -> InboxItem:
        item = self._require_editable(item_id)
        authors = self._resolve_many(Author, author_ids)
        series = self._resolve_one(Series, series_id)
        tags = self._resolve_many(Tag, tag_ids)

        item.display_name = display_name
        item.authors = authors
        item.series = series
        item.tags = tags
        self.session.commit()
        return item

    def bulk_update_metadata(
        self,
        *,
        item_ids: list[str],
        append_author_ids: list[str],
        append_tag_ids: list[str],
        replace_series: bool,
        replace_series_id: str | None,
    ) -> int:
        unique_item_ids = _unique(item_ids)
        items = [self._require_editable(item_id) for item_id in unique_item_ids]
        authors = self._resolve_many(Author, append_author_ids)
        tags = self._resolve_many(Tag, append_tag_ids)
        series = (
            self._resolve_one(Series, replace_series_id)
            if replace_series
            else None
        )

        for item in items:
            existing_author_ids = {author.id for author in item.authors}
            item.authors.extend(
                author for author in authors if author.id not in existing_author_ids
            )
            existing_tag_ids = {tag.id for tag in item.tags}
            item.tags.extend(tag for tag in tags if tag.id not in existing_tag_ids)
            if replace_series:
                item.series = series

        self.session.commit()
        return len(items)

    def _require_editable(self, item_id: str) -> InboxItem:
        item = self.items.get(item_id)
        if item is None:
            raise InboxItemNotFound(item_id)
        if item.status is not InboxStatus.READY_TO_TAG:
            raise InboxActionNotAllowed(item.status)
        return item

    def _resolve_many(self, model: type, entry_ids: list[str]) -> list:
        unique_ids = _unique(entry_ids)
        if not unique_ids:
            return []
        entries = list(
            self.session.scalars(select(model).where(model.id.in_(unique_ids)))
        )
        by_id = {entry.id: entry for entry in entries}
        missing = [entry_id for entry_id in unique_ids if entry_id not in by_id]
        if missing:
            raise TaxonomySelectionNotFound(missing[0])
        return [by_id[entry_id] for entry_id in unique_ids]

    def _resolve_one(self, model: type, entry_id: str | None):
        if entry_id is None:
            return None
        entry = self.session.get(model, entry_id)
        if entry is None:
            raise TaxonomySelectionNotFound(entry_id)
        return entry


class ScanService(InboxService):
    def __init__(self, *, session: Session, storage: LocalStorage) -> None:
        super().__init__(session)
        self.scanner = InboxScanner(session=session, storage=storage)

    def scan(self, now: datetime | None = None) -> ScanResult:
        stable_candidates = self.scanner.observe(now=now)
        created_items = 0

        for stable in stable_candidates:
            canonical_path = str(stable.snapshot.path)
            if self.items.has_source(canonical_path):
                continue

            item = InboxItem(
                media_type=stable.media_type,
                status=InboxStatus.READY_TO_TAG,
                suggested_actual_filename=stable.path.name,
            )
            item.sources.append(
                SourceFile.from_snapshot(stable.snapshot, is_primary=True)
            )
            self.session.add(item)
            self.session.flush()

            event_time = utc_now()
            self.session.add_all(
                [
                    InboxStatusEvent(
                        inbox_item_id=item.id,
                        from_status=None,
                        to_status=InboxStatus.DISCOVERED,
                        reason="stable candidate discovered",
                        created_at=event_time,
                    ),
                    InboxStatusEvent(
                        inbox_item_id=item.id,
                        from_status=InboxStatus.DISCOVERED,
                        to_status=InboxStatus.ANALYZING,
                        reason="direct media analysis started",
                        created_at=event_time + timedelta(microseconds=1),
                    ),
                    InboxStatusEvent(
                        inbox_item_id=item.id,
                        from_status=InboxStatus.ANALYZING,
                        to_status=InboxStatus.READY_TO_TAG,
                        reason="direct media is ready for metadata",
                        created_at=event_time + timedelta(microseconds=2),
                    ),
                ]
            )
            candidate = self.session.scalar(
                select(ScanCandidate).where(
                    ScanCandidate.canonical_path == canonical_path
                )
            )
            if candidate is not None:
                candidate.is_claimed = True
            created_items += 1

        self.session.commit()
        return ScanResult(
            observed_candidates=self.scanner.last_observed_count,
            stable_candidates=len(stable_candidates),
            created_items=created_items,
        )


def _unique(values: list[str]) -> list[str]:
    return list(dict.fromkeys(values))
