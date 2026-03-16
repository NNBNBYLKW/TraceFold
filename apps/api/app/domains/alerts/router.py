from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.alerts import service
from app.domains.alerts.schemas import AlertResultListRead, AlertResultRead


router = APIRouter()


@router.get("", response_model=ApiResponse[AlertResultListRead])
def list_alert_results(
    source_domain: str,
    source_record_id: int | None = None,
    status: str | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertResultListRead]:
    result = service.list_alert_reads(
        db,
        source_domain=source_domain,
        source_record_id=source_record_id,
        status=status,
    )
    return success_response(data=result, message="Alert results fetched.")


@router.post("/{alert_id}/viewed", response_model=ApiResponse[AlertResultRead])
def mark_alert_as_viewed(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertResultRead]:
    result = service.mark_alert_as_viewed(db, alert_id=alert_id)
    return success_response(data=result, message="Alert marked as viewed.")


@router.post("/{alert_id}/dismissed", response_model=ApiResponse[AlertResultRead])
def dismiss_alert(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertResultRead]:
    result = service.dismiss_alert(db, alert_id=alert_id)
    return success_response(data=result, message="Alert dismissed.")
