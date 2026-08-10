from __future__ import annotations

from pydantic import BaseModel, ConfigDict

from media_api.catalog.conflicts import (
    ConflictDetails,
    ConflictResolution,
    ConflictResult,
)
from media_api.catalog.ingest import IngestResult
from media_api.catalog.models import MediaAsset
from media_api.intake.schemas import InboxItemRead


class IngestResultRead(BaseModel):
    kind: str
    operation_id: str
    asset_id: str | None
    conflict_id: str | None
    cleanup_warning: str | None

    @classmethod
    def from_result(cls, result: IngestResult) -> IngestResultRead:
        return cls(
            kind=result.kind,
            operation_id=result.operation_id,
            asset_id=result.asset_id,
            conflict_id=result.conflict_id,
            cleanup_warning=result.cleanup_warning,
        )


class MediaAssetRead(BaseModel):
    id: str
    media_type: str
    actual_filename: str
    display_name: str | None
    author_ids: list[str]
    series_id: str | None
    tag_ids: list[str]
    size: int
    ingested_at: str

    @classmethod
    def from_model(cls, asset: MediaAsset) -> MediaAssetRead:
        return cls(
            id=asset.id,
            media_type=asset.media_type,
            actual_filename=asset.actual_filename,
            display_name=asset.display_name,
            author_ids=[author.id for author in asset.authors],
            series_id=asset.series_id,
            tag_ids=[tag.id for tag in asset.tags],
            size=asset.size,
            ingested_at=asset.ingested_at.isoformat(),
        )


class MediaAssetMetadataPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    display_name: str | None
    author_ids: list[str]
    series_id: str | None
    tag_ids: list[str]


class ConflictResolutionRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    resolution: ConflictResolution


class ConflictResolutionRead(BaseModel):
    operation_id: str
    resolution: ConflictResolution
    kind: str
    asset_id: str | None
    cleanup_warning: str | None

    @classmethod
    def from_result(cls, result: ConflictResult) -> ConflictResolutionRead:
        return cls(
            operation_id=result.operation_id,
            resolution=result.resolution,
            kind=result.kind,
            asset_id=result.asset_id,
            cleanup_warning=result.cleanup_warning,
        )


class ConflictRead(BaseModel):
    operation_id: str
    conflict_kinds: list[str]
    incoming_item: InboxItemRead
    existing_asset: MediaAssetRead | None
    incoming_preview_url: str
    existing_preview_url: str | None
    source_hash: str
    requested_actual_filename: str

    @classmethod
    def from_details(cls, details: ConflictDetails) -> ConflictRead:
        operation = details.operation
        existing = details.existing_asset
        if operation.source_hash is None:
            raise ValueError("conflict operation has no source hash")
        return cls(
            operation_id=operation.id,
            conflict_kinds=list(operation.conflict_kinds_json or []),
            incoming_item=InboxItemRead.from_model(details.item),
            existing_asset=(
                MediaAssetRead.from_model(existing) if existing is not None else None
            ),
            incoming_preview_url=f"/api/inbox/items/{details.item.id}/preview",
            existing_preview_url=(
                f"/api/library/assets/{existing.id}/preview"
                if existing is not None
                else None
            ),
            source_hash=operation.source_hash,
            requested_actual_filename=operation.requested_actual_filename,
        )
