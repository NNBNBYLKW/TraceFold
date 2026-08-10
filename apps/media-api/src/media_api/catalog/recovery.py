from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState, MediaAsset
from media_api.catalog.repository import IngestOperationRepository
from media_api.intake.models import InboxStatusEvent
from media_api.intake.repository import InboxRepository
from media_api.intake.state import InboxStatus
from media_api.storage.contracts import StoragePort, TargetExistsError
from media_api.taxonomy.models import Author, Series, Tag


class RecoveryError(RuntimeError):
    pass


class RecoveryRequiresAttention(RecoveryError):
    pass


@dataclass(frozen=True)
class RecoveryResult:
    operation_id: str
    state: str
    warning_code: str | None = None


class IngestRecoveryService:
    def __init__(self, *, session: Session, storage: StoragePort) -> None:
        self.session = session
        self.storage = storage
        self.operations = IngestOperationRepository(session)
        self.items = InboxRepository(session)
        self.ingest = IngestService(session=session, storage=storage)

    def recover_all(self) -> list[RecoveryResult]:
        operation_ids = [operation.id for operation in self.operations.recoverable()]
        return [self.recover(operation_id) for operation_id in operation_ids]

    def recover(self, operation_id: str) -> RecoveryResult:
        operation = self.operations.require(operation_id)
        if operation.conflict_kinds_json:
            return self._result(operation)
        if operation.state in {
            IngestOperationState.COMPLETED,
            IngestOperationState.FAILED,
        }:
            return self._result(operation)

        final = self._final_path(operation)
        if operation.state is IngestOperationState.PREPARED:
            self._require_sources(operation)
            if final.exists():
                self._require_hash(final, operation.source_hash, label="final file")
                operation.final_relative_path = final.name
                operation.state = IngestOperationState.FILE_COMMITTED
                self.session.commit()
            else:
                staged = self._staged_path(operation)
                if (
                    operation.resolution == "replace"
                    and not staged.is_file()
                ):
                    return self._restore_replacement(operation)
                self._require_hash(staged, operation.source_hash, label="staged file")
                try:
                    final = self.storage.commit_stage(
                        staged,
                        Path(operation.requested_actual_filename),
                    )
                except TargetExistsError:
                    final = self._final_path(operation)
                    self._require_hash(final, operation.source_hash, label="final file")
                operation.final_relative_path = final.name
                operation.state = IngestOperationState.FILE_COMMITTED
                self.session.commit()

        if operation.state is IngestOperationState.FILE_COMMITTED:
            self._require_sources(operation)
            final = self._final_path(operation)
            self._require_hash(final, operation.source_hash, label="final file")
            operation = self.ingest.finish_database_commit(operation.id)

        if operation.state in {
            IngestOperationState.DB_COMMITTED,
            IngestOperationState.CLEANUP_PENDING,
        }:
            final = self._final_path(operation)
            self._require_hash(final, operation.source_hash, label="final file")
            return self.retry_cleanup(operation.id)

        return self._result(operation)

    def _restore_replacement(self, operation: IngestOperation) -> RecoveryResult:
        snapshot = operation.replacement_snapshot_json
        if (
            snapshot is None
            or operation.replacement_asset_id is None
            or operation.backup_path is None
        ):
            raise RecoveryRequiresAttention("replacement rollback data is incomplete")
        asset = self.session.get(MediaAsset, operation.replacement_asset_id)
        if asset is None:
            raise RecoveryRequiresAttention("replacement asset is missing")
        backup = Path(operation.backup_path)
        target = self._final_path(operation)
        old_hash = snapshot.get("sha256")
        if not isinstance(old_hash, str):
            raise RecoveryRequiresAttention("replacement snapshot hash is missing")
        self._require_hash(backup, old_hash, label="replacement backup")
        if target.exists():
            self._require_hash(target, old_hash, label="restored final file")
            self.storage.delete_verified(backup, old_hash)
        else:
            self.storage.restore_backup(backup, target)
        self.storage.remove_empty_backup(operation.id)
        self.storage.remove_empty_stage(operation.id)
        self._restore_asset_metadata(asset, snapshot)

        item = self.items.get(operation.inbox_item_id)
        if item is None:
            raise RecoveryRequiresAttention("replacement inbox item is missing")
        previous = item.status
        item.status = InboxStatus.READY_TO_TAG
        operation.state = IngestOperationState.FAILED
        operation.diagnostic_message = "replacement rolled back during recovery"
        self.session.add(
            InboxStatusEvent(
                inbox_item_id=item.id,
                from_status=previous,
                to_status=InboxStatus.READY_TO_TAG,
                reason="interrupted replacement restored the previous asset",
            )
        )
        self.session.commit()
        return self._result(operation)

    def _restore_asset_metadata(
        self,
        asset: MediaAsset,
        snapshot: dict[str, object],
    ) -> None:
        asset.media_type = str(snapshot["media_type"])
        asset.actual_filename = str(snapshot["actual_filename"])
        asset.final_relative_path = str(snapshot["final_relative_path"])
        asset.display_name = _optional_string(snapshot.get("display_name"))
        asset.size = int(snapshot["size"])
        asset.sha256 = str(snapshot["sha256"])
        asset.source_created_at = _optional_datetime(snapshot.get("source_created_at"))
        asset.preview_path = _optional_string(snapshot.get("preview_path"))
        asset.preview_sha256 = _optional_string(snapshot.get("preview_sha256"))
        asset.created_at = datetime.fromisoformat(str(snapshot["created_at"]))
        asset.ingested_at = datetime.fromisoformat(str(snapshot["ingested_at"]))
        asset.updated_at = datetime.fromisoformat(str(snapshot["updated_at"]))
        author_ids = [str(value) for value in snapshot.get("author_ids", [])]
        tag_ids = [str(value) for value in snapshot.get("tag_ids", [])]
        asset.authors = [
            author
            for author_id in author_ids
            if (author := self.session.get(Author, author_id)) is not None
        ]
        asset.tags = [
            tag
            for tag_id in tag_ids
            if (tag := self.session.get(Tag, tag_id)) is not None
        ]
        series_id = snapshot.get("series_id")
        asset.series = (
            self.session.get(Series, str(series_id))
            if series_id is not None
            else None
        )

    def finish_database_commit(self, operation_id: str) -> RecoveryResult:
        operation = self.operations.require(operation_id)
        if operation.state is IngestOperationState.FILE_COMMITTED:
            self._require_sources(operation)
            self._require_hash(
                self._final_path(operation),
                operation.source_hash,
                label="final file",
            )
            operation = self.ingest.finish_database_commit(operation.id)
        return self._result(operation)

    def retry_cleanup(self, operation_id: str) -> RecoveryResult:
        operation = self.operations.require(operation_id)
        if operation.state is IngestOperationState.COMPLETED:
            return self._result(operation)
        ingest_result = self.ingest.retry_cleanup(operation_id)
        operation = self.operations.require(operation_id)
        return RecoveryResult(
            operation_id=operation.id,
            state=operation.state.value,
            warning_code=ingest_result.cleanup_warning,
        )

    def _require_sources(self, operation: IngestOperation) -> None:
        item = self.items.get(operation.inbox_item_id)
        if item is None:
            raise RecoveryRequiresAttention("source owner inbox item is missing")
        for source in item.sources:
            path = Path(source.canonical_path)
            if not path.is_file():
                raise RecoveryRequiresAttention(
                    f"source file is missing before database commit: {path}"
                )
            self._require_hash(path, source.content_sha256, label="source file")
        final_input = self.ingest.final_input_path(item.id)
        source_paths = {Path(source.canonical_path).resolve() for source in item.sources}
        if final_input.resolve() not in source_paths:
            self._require_hash(
                final_input,
                operation.source_hash,
                label="processing output",
            )

    def _require_hash(
        self,
        path: Path,
        expected_hash: str | None,
        *,
        label: str,
    ) -> None:
        if expected_hash is None:
            raise RecoveryRequiresAttention(f"{label} has no recorded hash")
        if not path.is_file():
            raise RecoveryRequiresAttention(f"{label} is missing: {path}")
        try:
            actual_hash = self.storage.sha256(path)
        except Exception as exc:
            raise RecoveryRequiresAttention(f"cannot verify {label}: {path}") from exc
        if actual_hash != expected_hash:
            raise RecoveryRequiresAttention(
                f"{label} hash differs from the recorded content: {path}"
            )

    def _staged_path(self, operation: IngestOperation) -> Path:
        if operation.staged_path is None:
            raise RecoveryRequiresAttention("prepared operation has no staged path")
        return Path(operation.staged_path)

    def _final_path(self, operation: IngestOperation) -> Path:
        relative = (
            operation.final_relative_path
            if operation.final_relative_path is not None
            else operation.requested_actual_filename
        )
        return self.storage.library_dir / relative

    @staticmethod
    def _result(operation: IngestOperation) -> RecoveryResult:
        warning = (
            "CLEANUP_PENDING"
            if operation.state is IngestOperationState.CLEANUP_PENDING
            else None
        )
        return RecoveryResult(
            operation_id=operation.id,
            state=operation.state.value,
            warning_code=warning,
        )


def _optional_string(value: object) -> str | None:
    return str(value) if value is not None else None


def _optional_datetime(value: object) -> datetime | None:
    return datetime.fromisoformat(str(value)) if value is not None else None
