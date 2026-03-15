from __future__ import annotations

from datetime import datetime

from sqlalchemy import Float, cast
from sqlalchemy.orm import Session

from app.domains.expense.models import ExpenseRecord


_SORT_COLUMNS = {
    "created_at": ExpenseRecord.created_at,
    "amount": cast(ExpenseRecord.amount, Float),
}


def get_expense_record_by_id(db: Session, expense_id: int) -> ExpenseRecord | None:
    return db.get(ExpenseRecord, expense_id)


def list_expense_records(
    db: Session,
    *,
    page: int,
    page_size: int,
    sort_by: str,
    sort_order: str,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    category: str | None = None,
    keyword: str | None = None,
) -> tuple[list[ExpenseRecord], int]:
    query = db.query(ExpenseRecord)

    if date_from is not None:
        query = query.filter(ExpenseRecord.created_at >= date_from)
    if date_to is not None:
        query = query.filter(ExpenseRecord.created_at <= date_to)
    if category is not None:
        query = query.filter(ExpenseRecord.category == category)
    if keyword is not None:
        query = query.filter(ExpenseRecord.note.ilike(f"%{keyword}%"))

    total = query.count()
    order_column = _SORT_COLUMNS[sort_by]
    order_clause = order_column.asc() if sort_order == "asc" else order_column.desc()
    items = (
        query.order_by(order_clause)
        .offset((page - 1) * page_size)
        .limit(page_size)
        .all()
    )
    return items, total
