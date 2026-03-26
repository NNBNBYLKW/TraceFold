from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends, status
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, created_response, success_response
from app.db.session import get_db
from app.domains.capture import service
from app.domains.capture.schemas import (
    CaptureDetailRead,
    CaptureListRead,
    CaptureSubmitRequest,
    CaptureSubmitResultRead,
)

router = APIRouter()


@router.get("", response_model=ApiResponse[CaptureListRead])
def list_capture_items(
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    status: str | None = None,
    source_type: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[CaptureListRead]:
    result = service.list_capture_reads(
        db,
        page=page,
        page_size=page_size,
        sort_by=sort_by,
        sort_order=sort_order,
        status=status,
        source_type=source_type,
        date_from=date_from,
        date_to=date_to,
    )
    return success_response(data=result, message="Capture items fetched.")


@router.get("/{capture_id}", response_model=ApiResponse[CaptureDetailRead])
def get_capture_item(
    capture_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[CaptureDetailRead]:
    result = service.get_capture_read(db, capture_id)
    return success_response(data=result, message="Capture item fetched.")


@router.post(
    "",
    response_model=ApiResponse[CaptureSubmitResultRead],
    status_code=status.HTTP_201_CREATED,
)
def submit_capture(
    payload: CaptureSubmitRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[CaptureSubmitResultRead]:
    result = service.submit_capture_and_process(
        db,
        raw_text=payload.raw_text,
        source_type=payload.source_type,
        source_ref=payload.source_ref,
    )
    return created_response(data=result, message="Capture submitted.")
