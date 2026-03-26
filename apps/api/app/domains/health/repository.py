from __future__ import annotations

from datetime import datetime

from sqlalchemy import func
from sqlalchemy.orm import Session

from app.domains.health.models import HealthRecord


_SORT_COLUMNS = {
    "created_at": HealthRecord.created_at,
    "metric_type": func.lower(HealthRecord.metric_type),
}


def get_health_record_by_id(db: Session, health_id: int) -> HealthRecord | None:
    return db.get(HealthRecord, health_id)


def get_health_record_by_source_pending_id(db: Session, source_pending_id: int) -> HealthRecord | None:
    return (
        db.query(HealthRecord)
        .filter(HealthRecord.source_pending_id == source_pending_id)
        .order_by(HealthRecord.id.desc())
        .first()
    )


def get_health_record_by_source_capture_id(db: Session, source_capture_id: int) -> HealthRecord | None:
    return (
        db.query(HealthRecord)
        .filter(HealthRecord.source_capture_id == source_capture_id)
        .order_by(HealthRecord.id.desc())
        .first()
    )


def list_health_records(
    db: Session,
    *,
    page: int,
    page_size: int,
    sort_by: str,
    sort_order: str,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    metric_type: str | None = None,
    keyword: str | None = None,
) -> tuple[list[HealthRecord], int]:
    query = db.query(HealthRecord)

    if date_from is not None:
        query = query.filter(HealthRecord.created_at >= date_from)
    if date_to is not None:
        query = query.filter(HealthRecord.created_at <= date_to)
    if metric_type is not None:
        query = query.filter(HealthRecord.metric_type == metric_type)
    if keyword is not None:
        query = query.filter(HealthRecord.note.ilike(f"%{keyword}%"))

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


def count_health_records_created_since(
    db: Session,
    *,
    created_from: datetime,
) -> int:
    return db.query(HealthRecord).filter(HealthRecord.created_at >= created_from).count()


def list_recent_health_records(
    db: Session,
    *,
    limit: int,
) -> list[HealthRecord]:
    return (
        db.query(HealthRecord)
        .order_by(HealthRecord.created_at.desc(), HealthRecord.id.desc())
        .limit(limit)
        .all()
    )


def list_recent_metric_types(
    db: Session,
    *,
    limit: int,
) -> list[str]:
    rows = (
        db.query(
            HealthRecord.metric_type,
            func.max(HealthRecord.created_at).label("latest_created_at"),
            func.max(HealthRecord.id).label("latest_id"),
        )
        .group_by(HealthRecord.metric_type)
        .order_by(
            func.max(HealthRecord.created_at).desc(),
            func.max(HealthRecord.id).desc(),
        )
        .limit(limit)
        .all()
    )
    return [str(row[0]) for row in rows]
