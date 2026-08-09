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


@dataclass(frozen=True)
class ScanResult:
    observed_candidates: int
    stable_candidates: int
    created_items: int


class InboxService:
    def __init__(self, session: Session) -> None:
        self.session = session
        self.items = InboxRepository(session)

    def list_items(self, *, include_completed: bool = False) -> list[InboxItem]:
        return self.items.list_items(include_completed=include_completed)

    def get_item(self, item_id: str) -> InboxItem | None:
        return self.items.get(item_id)


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
