from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.dashboard.schemas import DashboardRead
from app.domains.dashboard.service import get_dashboard_read


router = APIRouter()


@router.get("", response_model=ApiResponse[DashboardRead])
def get_dashboard(
    db: Session = Depends(get_db),
) -> ApiResponse[DashboardRead]:
    result = get_dashboard_read(db)
    return success_response(data=result, message="Dashboard fetched.")
