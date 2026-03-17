from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.pending import service
from app.domains.pending.schemas import (
    PendingActionResultRead,
    PendingConfirmRequest,
    PendingDetailRead,
    PendingDiscardRequest,
    PendingFixTextRequest,
    PendingListRead,
)

router = APIRouter()


@router.get("", response_model=ApiResponse[PendingListRead])
def list_pending_items(
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    status: str | None = None,
    target_domain: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[PendingListRead]:
    result = service.list_pending_reads(
        db,
        page=page,
        page_size=page_size,
        sort_by=sort_by,
        sort_order=sort_order,
        status=status,
        target_domain=target_domain,
        date_from=date_from,
        date_to=date_to,
    )
    return success_response(data=result, message="Pending items fetched.")


@router.get("/{pending_item_id}", response_model=ApiResponse[PendingDetailRead])
def get_pending_item(
    pending_item_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[PendingDetailRead]:
    result = service.get_pending_read(db, pending_item_id)
    return success_response(data=result, message="Pending item fetched.")


@router.post("/{pending_item_id}/confirm", response_model=ApiResponse[PendingActionResultRead])
def confirm_pending_item(
    pending_item_id: int,
    payload: PendingConfirmRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[PendingActionResultRead]:
    result = service.apply_pending_confirm_action(
        db,
        pending_item_id=pending_item_id,
        note=payload.note,
    )
    return success_response(data=result, message="Pending item confirmed.")


@router.post("/{pending_item_id}/discard", response_model=ApiResponse[PendingActionResultRead])
def discard_pending_item(
    pending_item_id: int,
    payload: PendingDiscardRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[PendingActionResultRead]:
    result = service.apply_pending_discard_action(
        db,
        pending_item_id=pending_item_id,
        note=payload.note,
    )
    return success_response(data=result, message="Pending item discarded.")


@router.post("/{pending_item_id}/fix", response_model=ApiResponse[PendingActionResultRead])
def fix_pending_item(
    pending_item_id: int,
    payload: PendingFixTextRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[PendingActionResultRead]:
    result = service.apply_pending_fix_action(
        db,
        pending_item_id=pending_item_id,
        correction_text=payload.correction_text,
    )
    return success_response(data=result, message="Pending item updated.")
