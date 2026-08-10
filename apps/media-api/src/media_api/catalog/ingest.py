from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Literal

from sqlalchemy.orm import Session

from media_api.catalog.models import (
    IngestOperation,
    IngestOperationState,
    MediaAsset,
)
from media_api.catalog.repository import (
    IngestOperationRepository,
    MediaAssetRepository,
)
from media_api.infrastructure.db import utc_now
from media_api.intake.models import InboxItem, InboxStatusEvent, SourceFile
from media_api.intake.repository import InboxRepository
from media_api.intake.state import InboxStatus
from media_api.storage.contracts import FileSnapshot, StoragePort


class IngestError(RuntimeError):
    pass


class InboxNotReady(IngestError):
    pass


class SourceChanged(IngestError):
    pass


class FinalInputNotFound(IngestError):
    pass


class IdempotencyKeyReused(IngestError):
    pass


@dataclass(frozen=True)
class IngestResult:
    kind: Literal["completed", "conflict"]
    operation_id: str
    asset_id: str | None
    conflict_id: str | None
    final_path: Path | None
    cleanup_warning: str | None


class IngestService:
    def __init__(self, *, session: Session, storage: StoragePort) -> None:
        self.session = session
        self.storage = storage
        self.items = InboxRepository(session)
        self.operations = IngestOperationRepository(session)
        self.assets = MediaAssetRepository(session)

    def prepare(self, item_id: str, idempotency_key: str) -> IngestOperation:
        if not idempotency_key.strip():
            raise IngestError("idempotency key must not be empty")
        existing = self.operations.by_idempotency_key(idempotency_key)
        if existing is not None:
            if existing.inbox_item_id != item_id:
                raise IdempotencyKeyReused(
                    "idempotency key belongs to a different inbox item"
                )
            return existing

        item = self.items.get(item_id)
        if item is None or item.status is not InboxStatus.READY_TO_TAG:
            raise InboxNotReady(item_id)

        primary = _primary_source(item)
        for source in item.sources:
            path = Path(source.canonical_path)
            current = self.storage.snapshot(path)
            discovery = FileSnapshot(
                path=path.resolve(),
                size=source.size,
                modified_ns=source.modified_ns,
            )
            if current != discovery:
                raise SourceChanged(item_id)
            source.content_sha256 = self.storage.sha256(path)

        conflict_kinds = self._detect_conflicts(
            item.suggested_actual_filename,
            primary.content_sha256,
        )
        if conflict_kinds:
            operation = IngestOperation(
                inbox_item_id=item.id,
                idempotency_key=idempotency_key,
                state=IngestOperationState.PREPARED,
                requested_actual_filename=item.suggested_actual_filename,
                source_hash=primary.content_sha256,
                conflict_kinds_json=conflict_kinds,
            )
            item.status = InboxStatus.CONFLICT
            self.session.add(operation)
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=InboxStatus.READY_TO_TAG,
                    to_status=InboxStatus.CONFLICT,
                    reason="formal library conflict detected before staging",
                )
            )
            self.session.commit()
            self.session.refresh(operation)
            return operation

        operation = IngestOperation(
            inbox_item_id=item.id,
            idempotency_key=idempotency_key,
            state=IngestOperationState.PREPARED,
            requested_actual_filename=item.suggested_actual_filename,
            source_hash=primary.content_sha256,
        )
        self.session.add(operation)
        self.session.commit()
        self.session.refresh(operation)

        try:
            staged = self.storage.copy_to_stage(
                operation.id,
                Path(primary.canonical_path),
                item.suggested_actual_filename,
            )
        except Exception as exc:
            operation.state = IngestOperationState.FAILED
            operation.diagnostic_message = str(exc)
            self.session.commit()
            raise

        operation.staged_path = str(staged)
        self.session.commit()
        self.session.refresh(operation)
        return operation

    def ingest(self, item_id: str, idempotency_key: str) -> IngestResult:
        operation = self.prepare(item_id, idempotency_key)
        if operation.conflict_kinds_json:
            return self._conflict_result(operation)
        if operation.state is IngestOperationState.COMPLETED:
            return self._result(operation)
        if operation.asset_id is not None and operation.state in {
            IngestOperationState.DB_COMMITTED,
            IngestOperationState.CLEANUP_PENDING,
        }:
            return self._cleanup(operation)
        if operation.staged_path is None:
            raise IngestError("prepared operation has no verified staged file")

        item = self.items.get(item_id)
        if item is None or item.status is not InboxStatus.READY_TO_TAG:
            raise InboxNotReady(item_id)

        item.status = InboxStatus.INGESTING
        self.session.add(
            InboxStatusEvent(
                inbox_item_id=item.id,
                from_status=InboxStatus.READY_TO_TAG,
                to_status=InboxStatus.INGESTING,
                reason="direct ingest started",
            )
        )
        self.session.commit()

        final_path = self.storage.commit_stage(
            Path(operation.staged_path),
            Path(operation.requested_actual_filename),
        )
        operation.state = IngestOperationState.FILE_COMMITTED
        operation.final_relative_path = final_path.name
        self.session.commit()

        operation = self.finish_database_commit(operation.id)
        operation.state = IngestOperationState.CLEANUP_PENDING
        self.session.commit()
        return self._cleanup(operation)

    def finish_database_commit(self, operation_id: str) -> IngestOperation:
        operation = self.operations.require(operation_id)
        if operation.asset_id is not None:
            if self.assets.get(operation.asset_id) is None:
                raise IngestError("ingest operation refers to a missing media asset")
            return operation
        if operation.final_relative_path is None or operation.source_hash is None:
            raise IngestError("file-committed operation is incomplete")

        item = self.items.get(operation.inbox_item_id)
        if item is None:
            raise IngestError("ingest operation refers to a missing inbox item")
        if item.status is not InboxStatus.INGESTING:
            previous = item.status
            item.status = InboxStatus.INGESTING
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=previous,
                    to_status=InboxStatus.INGESTING,
                    reason="interrupted ingest resumed",
                    created_at=utc_now(),
                )
            )

        final_path = self.storage.library_dir / operation.final_relative_path
        primary = _primary_source(item)
        final_snapshot = self.storage.snapshot(final_path)
        asset = MediaAsset(
            media_type=item.media_type,
            actual_filename=final_path.name,
            final_relative_path=operation.final_relative_path,
            display_name=item.display_name,
            series=item.series,
            size=final_snapshot.size,
            sha256=operation.source_hash,
            source_created_at=datetime.fromtimestamp(
                primary.modified_ns / 1_000_000_000,
                tz=UTC,
            ),
            authors=list(item.authors),
            tags=list(item.tags),
        )
        self.session.add(asset)
        self.session.flush()
        item.status = InboxStatus.COMPLETED
        operation.asset_id = asset.id
        operation.state = IngestOperationState.DB_COMMITTED
        operation.diagnostic_message = None
        self.session.add(
            InboxStatusEvent(
                inbox_item_id=item.id,
                from_status=InboxStatus.INGESTING,
                to_status=InboxStatus.COMPLETED,
                reason="formal media asset committed",
                created_at=utc_now(),
            )
        )
        self.session.commit()
        self.session.refresh(operation)
        return operation

    def retry_cleanup(self, operation_id: str) -> IngestResult:
        operation = self.operations.require(operation_id)
        if operation.state is IngestOperationState.COMPLETED:
            return self._result(operation)
        if operation.state not in {
            IngestOperationState.DB_COMMITTED,
            IngestOperationState.CLEANUP_PENDING,
        }:
            raise IngestError("cleanup is allowed only after database commit")
        return self._cleanup(operation)

    def _cleanup(self, operation: IngestOperation) -> IngestResult:
        item = self.items.get(operation.inbox_item_id)
        if item is None or operation.asset_id is None:
            raise IngestError("committed ingest operation is incomplete")

        try:
            for source in item.sources:
                if source.content_sha256 is None:
                    raise IngestError("source hash is missing during cleanup")
                self.storage.delete_verified(
                    Path(source.canonical_path),
                    source.content_sha256,
                )
                source.is_present = False
            if operation.staged_path is not None and operation.source_hash is not None:
                self.storage.delete_verified(
                    Path(operation.staged_path),
                    operation.source_hash,
                )
            self.storage.remove_empty_stage(operation.id)
        except Exception as exc:
            operation.state = IngestOperationState.CLEANUP_PENDING
            operation.diagnostic_message = str(exc)
            self.session.commit()
            return self._result(operation, cleanup_warning="CLEANUP_PENDING")

        operation.state = IngestOperationState.COMPLETED
        operation.diagnostic_message = None
        self.session.commit()
        return self._result(operation)

    def _detect_conflicts(
        self,
        actual_filename: str,
        digest: str | None,
    ) -> list[str]:
        conflict_kinds: list[str] = []
        if (
            (self.storage.library_dir / actual_filename).exists()
            or self.assets.by_actual_filename(actual_filename) is not None
        ):
            conflict_kinds.append("name")
        if digest is not None and self.assets.by_sha256(digest) is not None:
            conflict_kinds.append("content")
        return conflict_kinds

    def _conflict_result(self, operation: IngestOperation) -> IngestResult:
        return IngestResult(
            kind="conflict",
            operation_id=operation.id,
            asset_id=None,
            conflict_id=operation.id,
            final_path=None,
            cleanup_warning=None,
        )

    def _result(
        self,
        operation: IngestOperation,
        *,
        cleanup_warning: str | None = None,
    ) -> IngestResult:
        final_path = (
            self.storage.library_dir / operation.final_relative_path
            if operation.final_relative_path is not None
            else None
        )
        return IngestResult(
            kind="completed",
            operation_id=operation.id,
            asset_id=operation.asset_id,
            conflict_id=None,
            final_path=final_path,
            cleanup_warning=cleanup_warning,
        )


def _primary_source(item: InboxItem) -> SourceFile:
    primary = [source for source in item.sources if source.is_primary]
    if len(primary) != 1:
        raise FinalInputNotFound(item.id)
    return primary[0]
