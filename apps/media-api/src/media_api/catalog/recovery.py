from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState
from media_api.catalog.repository import IngestOperationRepository
from media_api.intake.repository import InboxRepository
from media_api.storage.contracts import StoragePort, TargetExistsError


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
