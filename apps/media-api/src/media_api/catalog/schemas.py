from __future__ import annotations

from pydantic import BaseModel, ConfigDict

from media_api.catalog.ingest import IngestResult
from media_api.catalog.models import MediaAsset


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
