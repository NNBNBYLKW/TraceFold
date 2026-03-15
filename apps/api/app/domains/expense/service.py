from __future__ import annotations

from datetime import datetime

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError, NotFoundError
from app.domains.expense import repository
from app.domains.expense.models import ExpenseRecord
from app.domains.expense.schemas import ExpenseDetailRead, ExpenseListItemRead, ExpenseListRead


_DEFAULT_SORT_BY = "created_at"
_ALLOWED_SORT_FIELDS = {"created_at", "amount"}
_ALLOWED_SORT_ORDERS = {"asc", "desc"}
_PREVIEW_LENGTH = 120


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


def list_expense_reads(
    db: Session,
    *,
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    category: str | None = None,
    keyword: str | None = None,
) -> ExpenseListRead:
    validated_page, validated_page_size = _validate_pagination(page=page, page_size=page_size)
    validated_sort_by = _validate_sort_by(sort_by, default=_DEFAULT_SORT_BY)
    validated_sort_order = _validate_sort_order(sort_order)
    _validate_date_range(date_from=date_from, date_to=date_to)

    records, total = repository.list_expense_records(
        db,
        page=validated_page,
        page_size=validated_page_size,
        sort_by=validated_sort_by,
        sort_order=validated_sort_order,
        date_from=date_from,
        date_to=date_to,
        category=_normalize_optional_text(category),
        keyword=_normalize_optional_text(keyword),
    )
    return ExpenseListRead(
        items=[_build_expense_list_item(record) for record in records],
        page=validated_page,
        page_size=validated_page_size,
        total=total,
    )


def get_expense_read(db: Session, expense_id: int) -> ExpenseDetailRead:
    record = repository.get_expense_record_by_id(db, expense_id)
    if record is None:
        raise NotFoundError(
            message=f"Expense record {expense_id} was not found.",
            code="EXPENSE_NOT_FOUND",
        )
    return ExpenseDetailRead(
        id=record.id,
        created_at=record.created_at,
        amount=record.amount,
        currency=record.currency,
        category=record.category,
        note=record.note,
        source_capture_id=record.source_capture_id,
        source_pending_id=record.source_pending_id,
    )


def _build_expense_list_item(record: ExpenseRecord) -> ExpenseListItemRead:
    return ExpenseListItemRead(
        created_at=record.created_at,
        amount=record.amount,
        currency=record.currency,
        category=record.category,
        note_preview=_build_preview(record.note),
        has_source_pending=record.source_pending_id is not None,
    )


def _build_preview(value: str | None) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    if len(normalized) <= _PREVIEW_LENGTH:
        return normalized
    return normalized[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _validate_pagination(*, page: int, page_size: int) -> tuple[int, int]:
    if page < 1:
        raise BadRequestError(
            message="page must be greater than or equal to 1.",
            code="INVALID_PAGE",
        )
    if page_size < 1 or page_size > 100:
        raise BadRequestError(
            message="page_size must be between 1 and 100.",
            code="INVALID_PAGE_SIZE",
        )
    return page, page_size


def _validate_sort_by(sort_by: str | None, *, default: str) -> str:
    if sort_by is None:
        return default
    normalized_sort_by = sort_by.strip()
    if normalized_sort_by not in _ALLOWED_SORT_FIELDS:
        allowed = ", ".join(sorted(_ALLOWED_SORT_FIELDS))
        raise BadRequestError(
            message=f"sort_by must be one of: {allowed}.",
            code="INVALID_SORT_BY",
        )
    return normalized_sort_by


def _validate_sort_order(sort_order: str) -> str:
    normalized_sort_order = sort_order.strip().lower()
    if normalized_sort_order not in _ALLOWED_SORT_ORDERS:
        raise BadRequestError(
            message="sort_order must be either asc or desc.",
            code="INVALID_SORT_ORDER",
        )
    return normalized_sort_order


def _validate_date_range(*, date_from: datetime | None, date_to: datetime | None) -> None:
    if date_from is not None and date_to is not None and date_from > date_to:
        raise BadRequestError(
            message="date_from must be earlier than or equal to date_to.",
            code="INVALID_DATE_RANGE",
        )
