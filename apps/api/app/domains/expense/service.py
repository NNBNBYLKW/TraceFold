from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.expense.models import ExpenseRecord


def create_expense_record(
    db: Session,
    *,
    source_capture_id: int,
    source_pending_id: int | None,
    payload: dict,
) -> ExpenseRecord:
    record = ExpenseRecord(
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        amount=str(payload.get("amount", "")),
        currency=str(payload.get("currency", "CNY")),
        category=payload.get("category"),
        note=payload.get("note"),
    )
    db.add(record)
    db.flush()
    return record