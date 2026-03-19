from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.domains.ai_derivations.schemas import AiDerivationResultListRead
from app.db.session import get_db
from app.domains.alerts.schemas import AlertResultListRead
from app.domains.health import service
from app.domains.health.schemas import HealthDetailRead, HealthListRead
from app.domains.workbench import service as workbench_service


router = APIRouter()


@router.get("", response_model=ApiResponse[HealthListRead])
def list_health_records(
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    metric_type: str | None = None,
    keyword: str | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[HealthListRead]:
    result = service.list_health_reads(
        db,
        page=page,
        page_size=page_size,
        sort_by=sort_by,
        sort_order=sort_order,
        date_from=date_from,
        date_to=date_to,
        metric_type=metric_type,
        keyword=keyword,
    )
    return success_response(data=result, message="Health records fetched.")


@router.get("/{health_id}", response_model=ApiResponse[HealthDetailRead])
def get_health_record(
    health_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[HealthDetailRead]:
    result = service.get_health_read(db, health_id)
    workbench_service.record_health_view_best_effort(db, health_read=result)
    return success_response(data=result, message="Health record fetched.")


@router.post("/{health_id}/rules/rerun", response_model=ApiResponse[AlertResultListRead])
def rerun_health_record_rules(
    health_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AlertResultListRead]:
    result = service.rerun_health_rules(db, health_id=health_id)
    return success_response(data=result, message="Health rules rerun completed.")


@router.post("/{health_id}/ai/health-summary/rerun", response_model=ApiResponse[AiDerivationResultListRead])
def rerun_health_record_ai_summary(
    health_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationResultListRead]:
    result = service.rerun_health_summary(db, health_id=health_id)
    return success_response(data=result, message="Health AI summary rerun completed.")
