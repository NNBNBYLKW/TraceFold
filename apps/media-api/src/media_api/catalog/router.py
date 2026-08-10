from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Header, HTTPException
from sqlalchemy.orm import Session, sessionmaker

from media_api.catalog.conflicts import (
    ConflictAlreadyResolved,
    ConflictChoiceExpired,
    ConflictIdempotencyKeyReused,
    ConflictNotFound,
    ConflictReplacementUnavailable,
    ConflictService,
)
from media_api.catalog.ingest import (
    InboxNotReady,
    IngestError,
    IngestService,
    SourceChanged,
)
from media_api.catalog.schemas import (
    ConflictRead,
    ConflictResolutionRead,
    ConflictResolutionRequest,
    IngestResultRead,
    MediaAssetMetadataPatch,
    MediaAssetRead,
)
from media_api.catalog.service import (
    CatalogService,
    CatalogTaxonomyNotFound,
    MediaAssetNotFound,
)
from media_api.storage.local import LocalStorage


def build_catalog_router(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
) -> APIRouter:
    router = APIRouter(prefix="/api", tags=["catalog"])

    @router.get("/conflicts/{operation_id}", response_model=ConflictRead)
    def get_conflict(operation_id: str) -> ConflictRead:
        with session_factory() as session:
            try:
                details = ConflictService(
                    session=session,
                    storage=storage,
                ).get_details(operation_id)
            except ConflictNotFound as exc:
                raise HTTPException(status_code=404, detail="Conflict not found") from exc
            return ConflictRead.from_details(details)

    @router.post(
        "/conflicts/{operation_id}/resolve",
        response_model=ConflictResolutionRead,
    )
    def resolve_conflict(
        operation_id: str,
        payload: ConflictResolutionRequest,
        idempotency_key: Annotated[str, Header(alias="Idempotency-Key")],
    ) -> ConflictResolutionRead:
        with session_factory() as session:
            try:
                result = ConflictService(
                    session=session,
                    storage=storage,
                ).resolve(
                    operation_id,
                    payload.resolution,
                    idempotency_key=idempotency_key,
                )
            except ConflictNotFound as exc:
                raise HTTPException(status_code=404, detail="Conflict not found") from exc
            except (
                ConflictAlreadyResolved,
                ConflictChoiceExpired,
                ConflictIdempotencyKeyReused,
                ConflictReplacementUnavailable,
            ) as exc:
                raise HTTPException(status_code=409, detail=str(exc)) from exc
            return ConflictResolutionRead.from_result(result)

    @router.post(
        "/inbox/items/{item_id}/ingest",
        response_model=IngestResultRead,
    )
    def ingest_item(
        item_id: str,
        idempotency_key: Annotated[str, Header(alias="Idempotency-Key")],
    ) -> IngestResultRead:
        with session_factory() as session:
            try:
                result = IngestService(
                    session=session,
                    storage=storage,
                ).ingest(item_id, idempotency_key)
            except SourceChanged as exc:
                raise HTTPException(status_code=409, detail="Source file changed") from exc
            except InboxNotReady as exc:
                raise HTTPException(status_code=409, detail="Inbox item is not ready") from exc
            except IngestError as exc:
                raise HTTPException(status_code=409, detail=str(exc)) from exc
            return IngestResultRead.from_result(result)

    @router.get("/library/assets", response_model=list[MediaAssetRead])
    def list_assets(search: str | None = None) -> list[MediaAssetRead]:
        with session_factory() as session:
            assets = CatalogService(session).list_assets(search=search)
            return [MediaAssetRead.from_model(asset) for asset in assets]

    @router.get("/library/assets/{asset_id}", response_model=MediaAssetRead)
    def get_asset(asset_id: str) -> MediaAssetRead:
        with session_factory() as session:
            try:
                asset = CatalogService(session).get_asset(asset_id)
            except MediaAssetNotFound as exc:
                raise HTTPException(status_code=404, detail="Media asset not found") from exc
            return MediaAssetRead.from_model(asset)

    @router.patch(
        "/library/assets/{asset_id}/metadata",
        response_model=MediaAssetRead,
    )
    def update_asset_metadata(
        asset_id: str,
        payload: MediaAssetMetadataPatch,
    ) -> MediaAssetRead:
        with session_factory() as session:
            try:
                asset = CatalogService(session).update_metadata(
                    asset_id,
                    display_name=payload.display_name,
                    author_ids=payload.author_ids,
                    series_id=payload.series_id,
                    tag_ids=payload.tag_ids,
                )
            except MediaAssetNotFound as exc:
                raise HTTPException(status_code=404, detail="Media asset not found") from exc
            except CatalogTaxonomyNotFound as exc:
                raise HTTPException(status_code=404, detail="Taxonomy entry not found") from exc
            return MediaAssetRead.from_model(asset)

    return router
