from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.alerts import service
from app.domains.alerts.schemas import AlertListRead, AlertRead, AlertResolveRequest


router = APIRouter()


@router.get("", response_model=ApiResponse[AlertListRead])
def list_alerts(
    domain: str | None = None,
    rule_key: str | None = None,
    status: str | None = None,
    limit: int = 50,
    source_domain: str | None = None,
    source_record_id: int | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertListRead]:
    result = service.list_alert_reads(
        db,
        domain=domain,
        rule_key=rule_key,
        status=status,
        limit=limit,
        source_domain=source_domain,
        source_record_id=source_record_id,
    )
    return success_response(data=result, message="Alerts fetched.")


@router.get("/{alert_id}", response_model=ApiResponse[AlertRead])
def get_alert(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertRead]:
    result = service.get_alert_read(db, alert_id)
    return success_response(data=result, message="Alert fetched.")


@router.post("/{alert_id}/acknowledge", response_model=ApiResponse[AlertRead])
def acknowledge_alert(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertRead]:
    result = service.acknowledge_alert(db, alert_id=alert_id)
    return success_response(data=result, message="Alert acknowledged.")


@router.post("/{alert_id}/resolve", response_model=ApiResponse[AlertRead])
def resolve_alert(
    alert_id: int,
    payload: AlertResolveRequest | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertRead]:
    result = service.resolve_alert(
        db,
        alert_id=alert_id,
        resolution_note=payload.resolution_note if payload is not None else None,
    )
    return success_response(data=result, message="Alert resolved.")


@router.post("/{alert_id}/viewed", response_model=ApiResponse[AlertRead], include_in_schema=False)
def mark_alert_as_viewed(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertRead]:
    result = service.mark_alert_as_viewed(db, alert_id=alert_id)
    return success_response(data=result, message="Alert acknowledged.")


@router.post("/{alert_id}/dismissed", response_model=ApiResponse[AlertRead], include_in_schema=False)
def dismiss_alert(
    alert_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertRead]:
    result = service.dismiss_alert(db, alert_id=alert_id)
    return success_response(data=result, message="Alert resolved.")
