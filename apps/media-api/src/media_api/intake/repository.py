from __future__ import annotations

from datetime import datetime

from sqlalchemy import select
from sqlalchemy.orm import Session, selectinload

from media_api.intake.models import InboxItem, ScanCandidate, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.contracts import FileSnapshot


class ScanCandidateRepository:
    def __init__(self, session: Session) -> None:
        self.session = session

    def observe(
        self,
        snapshot: FileSnapshot,
        *,
        extension: str,
        media_type: str | None,
        observed_at: datetime,
    ) -> ScanCandidate:
        canonical_path = str(snapshot.path)
        candidate = self.session.scalar(
            select(ScanCandidate).where(
                ScanCandidate.canonical_path == canonical_path
            )
        )
        if candidate is None:
            candidate = ScanCandidate(
                canonical_path=canonical_path,
                extension=extension,
                media_type=media_type,
                size=snapshot.size,
                modified_ns=snapshot.modified_ns,
                stable_observations=1,
                is_stable=False,
                is_claimed=False,
                first_seen_at=observed_at,
                last_seen_at=observed_at,
            )
            self.session.add(candidate)
            return candidate

        unchanged = (
            candidate.size == snapshot.size
            and candidate.modified_ns == snapshot.modified_ns
        )
        candidate.extension = extension
        candidate.media_type = media_type
        candidate.size = snapshot.size
        candidate.modified_ns = snapshot.modified_ns
        candidate.last_seen_at = observed_at
        if unchanged:
            candidate.stable_observations += 1
        else:
            candidate.stable_observations = 1
            candidate.is_stable = False
            candidate.first_seen_at = observed_at
            if not self._has_active_source(canonical_path):
                candidate.is_claimed = False
        if candidate.stable_observations >= 2:
            candidate.is_stable = True
        return candidate

    def release_missing(
        self,
        observed_paths: set[str],
        *,
        observed_at: datetime,
    ) -> None:
        for candidate in self.session.scalars(
            select(ScanCandidate).where(ScanCandidate.is_claimed.is_(True))
        ):
            if candidate.canonical_path in observed_paths:
                continue
            if self._has_active_source(candidate.canonical_path):
                continue
            candidate.is_claimed = False
            candidate.is_stable = False
            candidate.stable_observations = 0
            candidate.first_seen_at = observed_at

    def _has_active_source(self, canonical_path: str) -> bool:
        return self.session.scalar(
            select(SourceFile.id)
            .join(InboxItem, InboxItem.id == SourceFile.inbox_item_id)
            .where(
                SourceFile.canonical_path == canonical_path,
                InboxItem.status != InboxStatus.COMPLETED,
            )
            .limit(1)
        ) is not None


class InboxRepository:
    def __init__(self, session: Session) -> None:
        self.session = session

    def list_items(self, *, include_completed: bool = False) -> list[InboxItem]:
        statement = (
            select(InboxItem)
            .options(
                selectinload(InboxItem.sources),
                selectinload(InboxItem.authors),
                selectinload(InboxItem.tags),
            )
            .order_by(InboxItem.discovered_at.desc(), InboxItem.id)
        )
        if not include_completed:
            statement = statement.where(InboxItem.status != InboxStatus.COMPLETED)
        return list(self.session.scalars(statement).unique())

    def get(self, item_id: str) -> InboxItem | None:
        return self.session.scalar(
            select(InboxItem)
            .where(InboxItem.id == item_id)
            .options(
                selectinload(InboxItem.sources),
                selectinload(InboxItem.authors),
                selectinload(InboxItem.tags),
            )
        )

    def has_source(self, canonical_path: str) -> bool:
        return (
            self.session.scalar(
                select(SourceFile.id)
                .join(InboxItem, InboxItem.id == SourceFile.inbox_item_id)
                .where(
                    SourceFile.canonical_path == canonical_path,
                    InboxItem.status != InboxStatus.COMPLETED,
                )
            )
            is not None
        )
