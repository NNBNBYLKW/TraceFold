from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta
from pathlib import Path
from threading import Lock

from sqlalchemy import select
from sqlalchemy.orm import Session, sessionmaker

from media_api.catalog.models import IngestOperation, IngestOperationState
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
from media_api.processing.planner import ProcessingCommandService
from media_api.processing.contracts import Processor
from media_api.processing.registry import ProcessorRegistry
from media_api.settings.models import ProcessorSetting
from media_api.storage.contracts import FileSnapshot
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


class ScanCoordinator:
    """Serialize every scan trigger across the application process."""

    def __init__(
        self,
        *,
        session_factory: sessionmaker[Session],
        storage: LocalStorage,
        registry: ProcessorRegistry | None = None,
    ) -> None:
        self.session_factory = session_factory
        self.storage = storage
        self.registry = registry
        self._lock = Lock()

    def scan(self, now: datetime | None = None) -> ScanResult:
        with self._lock:
            with self.session_factory() as session:
                return ScanService(
                    session=session,
                    storage=self.storage,
                    registry=self.registry,
                ).scan(now=now)


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
    def __init__(
        self,
        *,
        session: Session,
        storage: LocalStorage,
        registry: ProcessorRegistry | None = None,
    ) -> None:
        super().__init__(session)
        self.scanner = InboxScanner(session=session, storage=storage)
        self.registry = registry or ProcessorRegistry()

    def scan(self, now: datetime | None = None) -> ScanResult:
        stable_candidates = self.scanner.observe(now=now)
        created_items = self._create_detected_items()
        self._upgrade_ready_items()

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

    def _create_detected_items(self) -> int:
        candidates = self._stable_unclaimed_candidates()
        detections = self.registry.claim_detections(
            (self._candidate_snapshot(candidate) for candidate in candidates),
            include=self._processor_enabled,
        )
        by_path = {
            self._path_key(Path(candidate.canonical_path)): candidate
            for candidate in candidates
        }
        created = 0
        for detection in detections:
            source_candidates = [
                by_path[self._path_key(snapshot.path)]
                for snapshot in detection.inputs
            ]
            item = InboxItem(
                media_type=detection.output_media_type,
                status=InboxStatus.DISCOVERED,
                suggested_actual_filename=detection.expected_output_name,
            )
            item.sources.append(
                SourceFile.from_snapshot(detection.primary, is_primary=True)
            )
            item.sources.extend(
                SourceFile.from_snapshot(snapshot, is_primary=False)
                for snapshot in detection.companions
            )
            self.session.add(item)
            self.session.flush()
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=None,
                    to_status=InboxStatus.DISCOVERED,
                    reason=f"{detection.processor_name} claimed stable source files",
                )
            )
            for candidate in source_candidates:
                candidate.is_claimed = True
            processor = self.registry.require(detection.processor_name)
            if self._processor_auto_enabled(processor):
                ProcessingCommandService(
                    session=self.session,
                    registry=self.registry,
                ).create_job(
                    item.id,
                    f"auto-process:{item.id}:1",
                    automatic=True,
                )
            else:
                self.session.commit()
            created += 1
        return created

    def _upgrade_ready_items(self) -> None:
        candidates = self._stable_unclaimed_candidates()
        if not candidates:
            return
        by_path = {
            self._path_key(Path(candidate.canonical_path)): candidate
            for candidate in candidates
        }
        for item in self.items.list_items():
            if item.status is not InboxStatus.READY_TO_TAG:
                continue
            active_operation = self.session.scalar(
                select(IngestOperation.id).where(
                    IngestOperation.inbox_item_id == item.id,
                    IngestOperation.state.not_in(
                        (
                            IngestOperationState.COMPLETED,
                            IngestOperationState.FAILED,
                        )
                    ),
                )
            )
            if active_operation is not None:
                continue
            available = [candidate for candidate in candidates if not candidate.is_claimed]
            if not available:
                return
            existing_snapshots = tuple(
                FileSnapshot(
                    path=Path(source.canonical_path),
                    size=source.size,
                    modified_ns=source.modified_ns,
                )
                for source in item.sources
            )
            detection = self.registry.find_upgrade(
                existing_snapshots,
                (self._candidate_snapshot(candidate) for candidate in available),
                include=self._processor_auto_enabled,
            )
            if detection is None:
                continue
            existing_keys = {
                self._path_key(Path(source.canonical_path)) for source in item.sources
            }
            additions = [
                snapshot
                for snapshot in detection.inputs
                if self._path_key(snapshot.path) not in existing_keys
            ]
            if not additions:
                continue
            for snapshot in additions:
                item.sources.append(
                    SourceFile.from_snapshot(snapshot, is_primary=False)
                )
                by_path[self._path_key(snapshot.path)].is_claimed = True
            self.session.flush()
            ProcessingCommandService(
                session=self.session,
                registry=self.registry,
            ).create_job(
                item.id,
                f"auto-process:{item.id}:{item.processing_plan_version + 1}",
                automatic=True,
            )

    def _stable_unclaimed_candidates(self) -> list[ScanCandidate]:
        return list(
            self.session.scalars(
                select(ScanCandidate)
                .where(
                    ScanCandidate.is_stable.is_(True),
                    ScanCandidate.is_claimed.is_(False),
                )
                .order_by(ScanCandidate.canonical_path)
            )
        )

    @staticmethod
    def _candidate_snapshot(candidate: ScanCandidate) -> FileSnapshot:
        return FileSnapshot(
            path=Path(candidate.canonical_path),
            size=candidate.size,
            modified_ns=candidate.modified_ns,
        )

    @staticmethod
    def _path_key(path: Path) -> str:
        return str(path.resolve()).casefold()

    def _processor_enabled(self, processor: Processor) -> bool:
        setting = self.session.get(ProcessorSetting, processor.descriptor.name)
        return setting is None or setting.enabled

    def _processor_auto_enabled(self, processor: Processor) -> bool:
        setting = self.session.get(ProcessorSetting, processor.descriptor.name)
        return (
            (setting is None or setting.enabled)
            and processor.descriptor.auto_run_default
            and (setting is None or setting.auto_run_default)
        )


def _unique(values: list[str]) -> list[str]:
    return list(dict.fromkeys(values))
