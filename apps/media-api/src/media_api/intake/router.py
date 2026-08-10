from __future__ import annotations

from pathlib import Path

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse
from sqlalchemy.orm import Session, sessionmaker

from media_api.intake.schemas import (
    BulkMetadataPatch,
    BulkMetadataResult,
    InboxItemRead,
    InboxMetadataPatch,
    ScanResultRead,
)
from media_api.intake.service import (
    InboxActionNotAllowed,
    InboxItemNotFound,
    InboxMetadataService,
    InboxService,
    ScanService,
    TaxonomySelectionNotFound,
)
from media_api.processing.registry import ProcessorRegistry
from media_api.storage.local import LocalStorage
from media_api.storage.local import ensure_within
from media_api.storage.contracts import UnsafePathError
from media_api.storage.preview import PreviewGenerationFailed, PreviewService


def build_inbox_router(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
    registry: ProcessorRegistry | None = None,
) -> APIRouter:
    router = APIRouter(prefix="/api/inbox", tags=["inbox"])

    @router.post("/scan", response_model=ScanResultRead)
    def scan() -> ScanResultRead:
        with session_factory() as session:
            result = ScanService(
                session=session,
                storage=storage,
                registry=registry,
            ).scan()
            return ScanResultRead.from_result(result)

    @router.get("/items", response_model=list[InboxItemRead])
    def list_items(include_completed: bool = False) -> list[InboxItemRead]:
        with session_factory() as session:
            items = InboxService(session).list_items(
                include_completed=include_completed
            )
            return [InboxItemRead.from_model(item) for item in items]

    @router.patch("/items/bulk-metadata", response_model=BulkMetadataResult)
    def bulk_metadata(payload: BulkMetadataPatch) -> BulkMetadataResult:
        with session_factory() as session:
            service = InboxMetadataService(session)
            try:
                updated_count = service.bulk_update_metadata(
                    item_ids=payload.item_ids,
                    append_author_ids=payload.append_author_ids,
                    append_tag_ids=payload.append_tag_ids,
                    replace_series=payload.replace_series,
                    replace_series_id=payload.replace_series_id,
                )
            except (InboxItemNotFound, TaxonomySelectionNotFound) as exc:
                raise HTTPException(status_code=404, detail=str(exc)) from exc
            except InboxActionNotAllowed as exc:
                raise HTTPException(
                    status_code=409,
                    detail="Inbox item metadata is not editable in its current state",
                ) from exc
            return BulkMetadataResult(updated_count=updated_count)

    @router.patch("/items/{item_id}/metadata", response_model=InboxItemRead)
    def update_metadata(
        item_id: str,
        payload: InboxMetadataPatch,
    ) -> InboxItemRead:
        with session_factory() as session:
            service = InboxMetadataService(session)
            try:
                item = service.update_metadata(
                    item_id,
                    display_name=payload.display_name,
                    author_ids=payload.author_ids,
                    series_id=payload.series_id,
                    tag_ids=payload.tag_ids,
                )
            except (InboxItemNotFound, TaxonomySelectionNotFound) as exc:
                raise HTTPException(status_code=404, detail=str(exc)) from exc
            except InboxActionNotAllowed as exc:
                raise HTTPException(
                    status_code=409,
                    detail="Inbox item metadata is not editable in its current state",
                ) from exc
            return InboxItemRead.from_model(item)

    @router.get("/items/{item_id}", response_model=InboxItemRead)
    def get_item(item_id: str) -> InboxItemRead:
        with session_factory() as session:
            item = InboxService(session).get_item(item_id)
            if item is None:
                raise HTTPException(status_code=404, detail="Inbox item not found")
            return InboxItemRead.from_model(item)

    @router.get("/items/{item_id}/preview", response_class=FileResponse)
    def preview_item(item_id: str) -> FileResponse:
        with session_factory() as session:
            item = InboxService(session).get_item(item_id)
            if item is None:
                raise HTTPException(status_code=404, detail="Inbox item not found")
            primary = next(
                (source for source in item.sources if source.is_primary),
                item.sources[0] if item.sources else None,
            )
            if primary is None:
                raise HTTPException(status_code=404, detail="Inbox media not found")
            try:
                media_path = ensure_within(Path(primary.canonical_path), storage.inbox_dir)
            except UnsafePathError as exc:
                raise HTTPException(status_code=404, detail="Inbox media not found") from exc
            if not media_path.is_file():
                raise HTTPException(
                    status_code=422,
                    detail={"kind": "placeholder", "code": "MEDIA_NOT_FOUND"},
                )
            try:
                digest = storage.sha256(media_path)
                preview = PreviewService(
                    work_dir=storage.work_dir,
                    registered_paths={media_path},
                ).get_or_create(media_path, item.media_type, digest)
            except PreviewGenerationFailed as exc:
                raise HTTPException(
                    status_code=422,
                    detail={"kind": "placeholder", "code": exc.code},
                ) from exc
            return FileResponse(preview, media_type="image/jpeg")

    return router
