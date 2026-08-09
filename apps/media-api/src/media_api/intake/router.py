from __future__ import annotations

from fastapi import APIRouter, HTTPException
from sqlalchemy.orm import Session, sessionmaker

from media_api.intake.schemas import InboxItemRead, ScanResultRead
from media_api.intake.service import InboxService, ScanService
from media_api.storage.local import LocalStorage


def build_inbox_router(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
) -> APIRouter:
    router = APIRouter(prefix="/api/inbox", tags=["inbox"])

    @router.post("/scan", response_model=ScanResultRead)
    def scan() -> ScanResultRead:
        with session_factory() as session:
            result = ScanService(session=session, storage=storage).scan()
            return ScanResultRead.from_result(result)

    @router.get("/items", response_model=list[InboxItemRead])
    def list_items(include_completed: bool = False) -> list[InboxItemRead]:
        with session_factory() as session:
            items = InboxService(session).list_items(
                include_completed=include_completed
            )
            return [InboxItemRead.from_model(item) for item in items]

    @router.get("/items/{item_id}", response_model=InboxItemRead)
    def get_item(item_id: str) -> InboxItemRead:
        with session_factory() as session:
            item = InboxService(session).get_item(item_id)
            if item is None:
                raise HTTPException(status_code=404, detail="Inbox item not found")
            return InboxItemRead.from_model(item)

    return router
