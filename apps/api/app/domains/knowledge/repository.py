from __future__ import annotations

from datetime import datetime

from sqlalchemy import func
from sqlalchemy.orm import Session

from app.domains.knowledge.models import KnowledgeEntry


_SORT_COLUMNS = {
    "created_at": KnowledgeEntry.created_at,
    "title": func.lower(func.coalesce(KnowledgeEntry.title, "")),
}


def get_knowledge_entry_by_id(db: Session, knowledge_id: int) -> KnowledgeEntry | None:
    return db.get(KnowledgeEntry, knowledge_id)


def get_knowledge_entry_by_source_pending_id(db: Session, source_pending_id: int) -> KnowledgeEntry | None:
    return (
        db.query(KnowledgeEntry)
        .filter(KnowledgeEntry.source_pending_id == source_pending_id)
        .order_by(KnowledgeEntry.id.desc())
        .first()
    )


def get_knowledge_entry_by_source_capture_id(db: Session, source_capture_id: int) -> KnowledgeEntry | None:
    return (
        db.query(KnowledgeEntry)
        .filter(KnowledgeEntry.source_capture_id == source_capture_id)
        .order_by(KnowledgeEntry.id.desc())
        .first()
    )


def list_knowledge_entries(
    db: Session,
    *,
    page: int,
    page_size: int,
    sort_by: str,
    sort_order: str,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    keyword: str | None = None,
    has_source_text: bool | None = None,
) -> tuple[list[KnowledgeEntry], int]:
    query = db.query(KnowledgeEntry)

    if date_from is not None:
        query = query.filter(KnowledgeEntry.created_at >= date_from)
    if date_to is not None:
        query = query.filter(KnowledgeEntry.created_at <= date_to)
    if keyword is not None:
        query = query.filter(
            KnowledgeEntry.title.ilike(f"%{keyword}%")
            | KnowledgeEntry.content.ilike(f"%{keyword}%")
        )
    if has_source_text is not None:
        source_text_present = func.length(func.trim(func.coalesce(KnowledgeEntry.source_text, ""))) > 0
        query = query.filter(source_text_present if has_source_text else ~source_text_present)

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


def count_knowledge_entries_created_since(
    db: Session,
    *,
    created_from: datetime,
) -> int:
    return db.query(KnowledgeEntry).filter(KnowledgeEntry.created_at >= created_from).count()


def list_recent_knowledge_entries(
    db: Session,
    *,
    limit: int,
) -> list[KnowledgeEntry]:
    return (
        db.query(KnowledgeEntry)
        .order_by(KnowledgeEntry.created_at.desc(), KnowledgeEntry.id.desc())
        .limit(limit)
        .all()
    )
