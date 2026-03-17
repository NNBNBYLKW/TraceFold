from __future__ import annotations

from fastapi import APIRouter, Depends, status
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, created_response
from app.db.session import get_db
from app.domains.capture import service
from app.domains.capture.schemas import CaptureSubmitRequest, CaptureSubmitResultRead

router = APIRouter()


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
