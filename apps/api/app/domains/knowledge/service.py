from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.knowledge.models import KnowledgeEntry


def create_knowledge_entry(
    db: Session,
    *,
    source_capture_id: int,
    source_pending_id: int | None,
    payload: dict,
) -> KnowledgeEntry:
    entry = KnowledgeEntry(
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        title=payload.get("title"),
        content=payload.get("content"),
        source_text=payload.get("source_text"),
    )
    db.add(entry)
    db.flush()
    return entry