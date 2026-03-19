from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.expense import service
from app.domains.expense.schemas import ExpenseDetailRead, ExpenseListRead
from app.domains.workbench import service as workbench_service


router = APIRouter()


@router.get("", response_model=ApiResponse[ExpenseListRead])
def list_expenses(
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    category: str | None = None,
    keyword: str | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[ExpenseListRead]:
    result = service.list_expense_reads(
        db,
        page=page,
        page_size=page_size,
        sort_by=sort_by,
        sort_order=sort_order,
        date_from=date_from,
        date_to=date_to,
        category=category,
        keyword=keyword,
    )
    return success_response(data=result, message="Expense records fetched.")


@router.get("/{expense_id}", response_model=ApiResponse[ExpenseDetailRead])
def get_expense(
    expense_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[ExpenseDetailRead]:
    result = service.get_expense_read(db, expense_id)
    workbench_service.record_expense_view_best_effort(db, expense_read=result)
    return success_response(data=result, message="Expense record fetched.")
