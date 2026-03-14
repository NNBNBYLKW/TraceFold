from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, ForeignKey, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class ExpenseRecord(IdMixin, Base):
    __tablename__ = "expense_records"

    source_capture_id: Mapped[int] = mapped_column(ForeignKey("capture_records.id"), nullable=False)
    source_pending_id: Mapped[int | None] = mapped_column(ForeignKey("pending_items.id"), nullable=True)
    amount: Mapped[str] = mapped_column(String(50), nullable=False)
    currency: Mapped[str] = mapped_column(String(20), nullable=False)
    category: Mapped[str | None] = mapped_column(String(100), nullable=True)
    note: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
