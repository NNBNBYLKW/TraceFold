from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, ForeignKey, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class KnowledgeEntry(IdMixin, Base):
    __tablename__ = "knowledge_entries"

    source_capture_id: Mapped[int] = mapped_column(ForeignKey("capture_records.id"), nullable=False)
    source_pending_id: Mapped[int | None] = mapped_column(ForeignKey("pending_items.id"), nullable=True)
    title: Mapped[str | None] = mapped_column(String(255), nullable=True)
    content: Mapped[str | None] = mapped_column(Text, nullable=True)
    source_text: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
