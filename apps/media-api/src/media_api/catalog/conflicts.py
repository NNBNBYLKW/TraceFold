from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path
from typing import Literal

from sqlalchemy.orm import Session

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import IngestOperation, IngestOperationState, MediaAsset
from media_api.catalog.repository import IngestOperationRepository, MediaAssetRepository
from media_api.intake.models import InboxItem, InboxStatusEvent
from media_api.intake.repository import InboxRepository
from media_api.intake.state import InboxStatus
from media_api.storage.contracts import StoragePort


class ConflictError(RuntimeError):
    pass


class ConflictNotFound(ConflictError):
    pass


class ConflictChoiceExpired(ConflictError):
    pass


class ConflictReplacementUnavailable(ConflictError):
    pass


class ConflictIdempotencyKeyReused(ConflictError):
    pass


class ConflictAlreadyResolved(ConflictError):
    pass


class ConflictResolution(StrEnum):
    SKIP = "skip"
    KEEP_BOTH = "keep_both"
    REPLACE = "replace"


@dataclass(frozen=True)
class ConflictDetection:
    kinds: frozenset[str]
    existing_asset_id: str | None


@dataclass(frozen=True)
class ConflictResult:
    operation_id: str
    resolution: ConflictResolution
    kind: Literal["skipped", "completed"]
    asset_id: str | None
    final_path: Path | None
    cleanup_warning: str | None


@dataclass(frozen=True)
class ConflictDetails:
    operation: IngestOperation
    item: InboxItem
    existing_asset: MediaAsset | None


class ConflictService:
    def __init__(self, *, session: Session, storage: StoragePort) -> None:
        self.session = session
        self.storage = storage
        self.operations = IngestOperationRepository(session)
        self.assets = MediaAssetRepository(session)
        self.items = InboxRepository(session)
        self.ingest = IngestService(session=session, storage=storage)

    def detect(self, *, actual_filename: str, sha256: str) -> ConflictDetection:
        name_asset = self.assets.by_actual_filename(actual_filename)
        content_asset = self.assets.by_sha256(sha256)
        kinds: set[str] = set()
        if name_asset is not None or (self.storage.library_dir / actual_filename).exists():
            kinds.add("name")
        if content_asset is not None:
            kinds.add("content")
        existing = name_asset or content_asset
        return ConflictDetection(
            kinds=frozenset(kinds),
            existing_asset_id=existing.id if existing is not None else None,
        )

    def resolve(
        self,
        operation_id: str,
        resolution: ConflictResolution,
        *,
        idempotency_key: str,
    ) -> ConflictResult:
        if not idempotency_key.strip():
            raise ConflictError("idempotency key must not be empty")
        operation = self.operations.get(operation_id)
        if operation is None:
            raise ConflictNotFound(operation_id)
        key_owner = self.operations.by_resolution_idempotency_key(idempotency_key)
        if key_owner is not None:
            if key_owner.id != operation.id or key_owner.resolution != resolution.value:
                raise ConflictIdempotencyKeyReused(
                    "idempotency key belongs to another conflict decision"
                )
            return self._resume_first_result(key_owner, resolution)
        if operation.resolution_idempotency_key is not None:
            raise ConflictAlreadyResolved(operation.id)
        if not operation.conflict_kinds_json:
            raise ConflictNotFound(operation_id)
        item = self.items.get(operation.inbox_item_id)
        if item is None or item.status is not InboxStatus.CONFLICT:
            raise ConflictNotFound(operation_id)
        self._require_unchanged_sources(operation)

        if resolution is ConflictResolution.SKIP:
            operation.resolution = resolution.value
            operation.resolution_idempotency_key = idempotency_key
            operation.state = IngestOperationState.SKIPPED
            item.status = InboxStatus.READY_TO_TAG
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=InboxStatus.CONFLICT,
                    to_status=InboxStatus.READY_TO_TAG,
                    reason="ingest conflict skipped by user",
                )
            )
            self.session.commit()
            return ConflictResult(
                operation_id=operation.id,
                resolution=resolution,
                kind="skipped",
                asset_id=None,
                final_path=None,
                cleanup_warning=None,
            )

        keep_both_filename: str | None = None
        replacement_snapshot: dict[str, object] | None = None
        if resolution is ConflictResolution.KEEP_BOTH:
            keep_both_filename = self.storage.next_available_name(
                operation.requested_actual_filename
            )
        else:
            asset = self._replacement_asset(operation)
            self._require_registered_file_unchanged(asset)
            replacement_snapshot = self._snapshot_asset(asset)

        operation.resolution = resolution.value
        operation.resolution_idempotency_key = idempotency_key
        if keep_both_filename is not None:
            operation.requested_actual_filename = keep_both_filename
            item.status = InboxStatus.READY_TO_TAG
            self.session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=InboxStatus.CONFLICT,
                    to_status=InboxStatus.READY_TO_TAG,
                    reason="keep-both target selected",
                )
            )
        else:
            operation.replacement_snapshot_json = replacement_snapshot
        operation.conflict_kinds_json = None
        self.session.commit()

        self.ingest.stage_operation(operation.id)
        ingest_result = self.ingest.resume(operation.id)
        return ConflictResult(
            operation_id=operation.id,
            resolution=resolution,
            kind="completed",
            asset_id=ingest_result.asset_id,
            final_path=ingest_result.final_path,
            cleanup_warning=ingest_result.cleanup_warning,
        )

    def get_details(self, operation_id: str) -> ConflictDetails:
        operation = self.operations.get(operation_id)
        if operation is None or not operation.conflict_kinds_json:
            raise ConflictNotFound(operation_id)
        item = self.items.get(operation.inbox_item_id)
        if item is None:
            raise ConflictNotFound(operation_id)
        existing = (
            self.assets.get(operation.replacement_asset_id)
            if operation.replacement_asset_id is not None
            else None
        )
        return ConflictDetails(
            operation=operation,
            item=item,
            existing_asset=existing,
        )

    def _resume_first_result(
        self,
        operation: IngestOperation,
        resolution: ConflictResolution,
    ) -> ConflictResult:
        if resolution is ConflictResolution.SKIP:
            return ConflictResult(
                operation_id=operation.id,
                resolution=resolution,
                kind="skipped",
                asset_id=None,
                final_path=None,
                cleanup_warning=None,
            )
        if operation.state is IngestOperationState.PREPARED and operation.staged_path is None:
            self.ingest.stage_operation(operation.id)
        ingest_result = self.ingest.resume(operation.id)
        return ConflictResult(
            operation_id=operation.id,
            resolution=resolution,
            kind="completed",
            asset_id=ingest_result.asset_id,
            final_path=ingest_result.final_path,
            cleanup_warning=ingest_result.cleanup_warning,
        )

    def _require_unchanged_sources(self, operation: IngestOperation) -> None:
        item = self.items.get(operation.inbox_item_id)
        if item is None or operation.source_hash is None:
            raise ConflictChoiceExpired(operation.id)
        try:
            for source in item.sources:
                if source.content_sha256 is None:
                    raise ConflictChoiceExpired(operation.id)
                if self.storage.sha256(Path(source.canonical_path)) != source.content_sha256:
                    raise ConflictChoiceExpired(operation.id)
            if self.storage.sha256(self.ingest.final_input_path(item.id)) != operation.source_hash:
                raise ConflictChoiceExpired(operation.id)
        except ConflictChoiceExpired:
            raise
        except Exception as exc:
            raise ConflictChoiceExpired(operation.id) from exc

    def _replacement_asset(self, operation: IngestOperation) -> MediaAsset:
        if operation.replacement_asset_id is None:
            raise ConflictReplacementUnavailable(
                "unregistered formal files cannot be replaced"
            )
        asset = self.assets.get(operation.replacement_asset_id)
        if asset is None:
            raise ConflictReplacementUnavailable("registered asset no longer exists")
        if asset.final_relative_path != operation.requested_actual_filename:
            raise ConflictReplacementUnavailable(
                "replacement is available only for the registered name conflict"
            )
        return asset

    def _require_registered_file_unchanged(self, asset: MediaAsset) -> None:
        path = self.storage.library_dir / asset.final_relative_path
        try:
            if self.storage.sha256(path) != asset.sha256:
                raise ConflictChoiceExpired(asset.id)
        except ConflictChoiceExpired:
            raise
        except Exception as exc:
            raise ConflictChoiceExpired(asset.id) from exc

    @staticmethod
    def _snapshot_asset(asset: MediaAsset) -> dict[str, object]:
        return {
            "media_type": asset.media_type,
            "actual_filename": asset.actual_filename,
            "final_relative_path": asset.final_relative_path,
            "display_name": asset.display_name,
            "series_id": asset.series_id,
            "size": asset.size,
            "sha256": asset.sha256,
            "source_created_at": (
                asset.source_created_at.isoformat()
                if asset.source_created_at is not None
                else None
            ),
            "preview_path": asset.preview_path,
            "preview_sha256": asset.preview_sha256,
            "created_at": asset.created_at.isoformat(),
            "ingested_at": asset.ingested_at.isoformat(),
            "updated_at": asset.updated_at.isoformat(),
            "author_ids": [author.id for author in asset.authors],
            "tag_ids": [tag.id for tag in asset.tags],
        }
