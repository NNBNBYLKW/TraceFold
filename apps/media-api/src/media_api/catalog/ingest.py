from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Literal

from sqlalchemy import select
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
from media_api.processing.models import ProcessingJob, ProcessingJobStatus
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
        final_input, final_input_hash = self._validated_final_input(item)

        from media_api.catalog.conflicts import ConflictService

        conflict = ConflictService(session=self.session, storage=self.storage).detect(
            actual_filename=item.suggested_actual_filename,
            sha256=final_input_hash,
        )
        if conflict.kinds:
            operation = IngestOperation(
                inbox_item_id=item.id,
                idempotency_key=idempotency_key,
                state=IngestOperationState.PREPARED,
                requested_actual_filename=item.suggested_actual_filename,
                source_hash=final_input_hash,
                conflict_kinds_json=[
                    kind for kind in ("name", "content") if kind in conflict.kinds
                ],
                replacement_asset_id=conflict.existing_asset_id,
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
            source_hash=final_input_hash,
        )
        self.session.add(operation)
        self.session.commit()
        self.session.refresh(operation)

        return self.stage_operation(operation.id)

    def ingest(self, item_id: str, idempotency_key: str) -> IngestResult:
        operation = self.prepare(item_id, idempotency_key)
        if operation.conflict_kinds_json:
            return self._conflict_result(operation)
        return self.resume(operation.id)

    def stage_operation(self, operation_id: str) -> IngestOperation:
        operation = self.operations.require(operation_id)
        if operation.staged_path is not None:
            return operation
        item = self.items.get(operation.inbox_item_id)
        if item is None or operation.source_hash is None:
            raise IngestError("prepared operation is incomplete")
        final_input, current_hash = self._validated_final_input(item)
        if current_hash != operation.source_hash:
            raise SourceChanged(item.id)
        try:
            staged = self.storage.copy_to_stage(
                operation.id,
                final_input,
                operation.requested_actual_filename,
            )
            if self.storage.sha256(staged) != operation.source_hash:
                raise SourceChanged("final input changed before staging completed")
        except Exception as exc:
            operation.diagnostic_message = str(exc)
            self.session.commit()
            raise
        operation.staged_path = str(staged)
        operation.diagnostic_message = None
        self.session.commit()
        self.session.refresh(operation)
        return operation

    def resume(self, operation_id: str) -> IngestResult:
        operation = self.operations.require(operation_id)
        if operation.conflict_kinds_json:
            raise IngestError("conflict must be resolved before ingest can resume")
        if operation.state is IngestOperationState.COMPLETED:
            return self._result(operation)
        if operation.asset_id is not None and operation.state in {
            IngestOperationState.DB_COMMITTED,
            IngestOperationState.CLEANUP_PENDING,
        }:
            return self._cleanup(operation)
        if operation.staged_path is None:
            raise IngestError("prepared operation has no verified staged file")

        item = self.items.get(operation.inbox_item_id)
        if item is None or item.status not in {
            InboxStatus.READY_TO_TAG,
            InboxStatus.CONFLICT,
            InboxStatus.INGESTING,
        }:
            raise InboxNotReady(operation.inbox_item_id)

        if operation.state is IngestOperationState.PREPARED:
            previous = item.status
            item.status = InboxStatus.INGESTING
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=previous,
                    to_status=InboxStatus.INGESTING,
                    reason="resolved ingest started",
                )
            )
            self.session.commit()

            if operation.resolution == "replace":
                final_path = self._commit_replacement_file(operation)
            else:
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

    def _commit_replacement_file(self, operation: IngestOperation) -> Path:
        if (
            operation.replacement_asset_id is None
            or operation.replacement_snapshot_json is None
            or operation.staged_path is None
        ):
            raise IngestError("replacement operation is incomplete")
        asset = self.assets.get(operation.replacement_asset_id)
        if asset is None:
            raise IngestError("replacement asset no longer exists")
        target = self.storage.library_dir / operation.requested_actual_filename
        if asset.final_relative_path != operation.requested_actual_filename:
            raise IngestError("replacement target does not match the registered asset")
        old_hash = str(operation.replacement_snapshot_json["sha256"])
        planned_backup = (
            self.storage.backups_dir / operation.id / operation.requested_actual_filename
        )
        if operation.backup_path is None:
            operation.backup_path = str(planned_backup)
            self.session.commit()
        if target.exists():
            if self.storage.sha256(target) != old_hash:
                raise SourceChanged("registered formal file changed before replacement")
            self.storage.backup_existing(operation.id, target)
        elif not planned_backup.is_file():
            raise IngestError("replacement target and backup are both missing")
        return self.storage.commit_stage(
            Path(operation.staged_path),
            Path(operation.requested_actual_filename),
        )

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
        if operation.resolution == "replace":
            if operation.replacement_asset_id is None:
                raise IngestError("replacement asset is missing")
            asset = self.assets.get(operation.replacement_asset_id)
            if asset is None:
                raise IngestError("replacement asset no longer exists")
            asset.media_type = item.media_type
            asset.actual_filename = final_path.name
            asset.final_relative_path = operation.final_relative_path
            asset.display_name = item.display_name
            asset.series = item.series
            asset.size = final_snapshot.size
            asset.sha256 = operation.source_hash
            asset.source_created_at = datetime.fromtimestamp(
                primary.modified_ns / 1_000_000_000,
                tz=UTC,
            )
            asset.preview_path = None
            asset.preview_sha256 = None
            asset.authors = list(item.authors)
            asset.tags = list(item.tags)
            asset.ingested_at = utc_now()
            asset.updated_at = utc_now()
        else:
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

    def final_input_path(self, item_id: str) -> Path:
        item = self.items.get(item_id)
        if item is None:
            raise FinalInputNotFound(item_id)
        job = self._completed_processing_job(item_id)
        if job is not None and job.output_path is not None:
            return Path(job.output_path)
        return Path(_primary_source(item).canonical_path)

    def _completed_processing_job(self, item_id: str) -> ProcessingJob | None:
        return self.session.scalar(
            select(ProcessingJob)
            .where(
                ProcessingJob.inbox_item_id == item_id,
                ProcessingJob.status == ProcessingJobStatus.COMPLETED,
                ProcessingJob.output_path.is_not(None),
            )
            .order_by(
                ProcessingJob.finished_at.desc(),
                ProcessingJob.created_at.desc(),
                ProcessingJob.id,
            )
            .limit(1)
        )

    def _validated_processing_output(
        self,
        job: ProcessingJob,
    ) -> tuple[Path, str]:
        if (
            job.output_path is None
            or job.output_size is None
            or job.output_modified_ns is None
            or job.output_sha256 is None
        ):
            raise SourceChanged("processing output has no persisted fingerprint")
        path = Path(job.output_path)
        current = self.storage.snapshot(path)
        if (
            current.size != job.output_size
            or current.modified_ns != job.output_modified_ns
            or self.storage.sha256(path) != job.output_sha256
        ):
            raise SourceChanged("processing output changed after validation")
        return path, job.output_sha256

    def _validated_final_input(self, item: InboxItem) -> tuple[Path, str]:
        processing_job = self._completed_processing_job(item.id)
        if processing_job is not None:
            return self._validated_processing_output(processing_job)
        final_input = Path(_primary_source(item).canonical_path)
        return final_input, self.storage.sha256(final_input)

    def _cleanup(self, operation: IngestOperation) -> IngestResult:
        item = self.items.get(operation.inbox_item_id)
        if item is None or operation.asset_id is None:
            raise IngestError("committed ingest operation is incomplete")
        if operation.source_hash is None:
            raise IngestError("committed ingest operation has no final input hash")

        try:
            for source in item.sources:
                if source.content_sha256 is None:
                    raise IngestError("source hash is missing during cleanup")
                self.storage.delete_verified(
                    Path(source.canonical_path),
                    source.content_sha256,
                )
                source.is_present = False
            source_paths = {
                Path(source.canonical_path).resolve() for source in item.sources
            }
            final_input = self.final_input_path(item.id).resolve()
            if final_input not in source_paths:
                self.storage.delete_verified(final_input, operation.source_hash)
            if operation.staged_path is not None and operation.source_hash is not None:
                self.storage.delete_verified(
                    Path(operation.staged_path),
                    operation.source_hash,
                )
            self.storage.remove_empty_stage(operation.id)
            if operation.backup_path is not None:
                snapshot = operation.replacement_snapshot_json or {}
                old_hash = snapshot.get("sha256")
                if not isinstance(old_hash, str):
                    raise IngestError("replacement backup hash is missing")
                self.storage.delete_verified(Path(operation.backup_path), old_hash)
                self.storage.remove_empty_backup(operation.id)
        except Exception as exc:
            operation.state = IngestOperationState.CLEANUP_PENDING
            operation.diagnostic_message = str(exc)
            self.session.commit()
            return self._result(operation, cleanup_warning="CLEANUP_PENDING")

        operation.state = IngestOperationState.COMPLETED
        operation.diagnostic_message = None
        self.session.commit()
        return self._result(operation)

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
