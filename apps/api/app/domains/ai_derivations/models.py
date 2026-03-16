from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import JSON, CheckConstraint, DateTime, Integer, String, Text, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class AiDerivationStatus:
    PENDING = "pending"
    COMPLETED = "completed"
    FAILED = "failed"


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class AiDerivationResult(IdMixin, Base):
    __tablename__ = "ai_derivation_results"
    __table_args__ = (
        UniqueConstraint(
            "target_domain",
            "target_record_id",
            "derivation_type",
            name="uq_ai_derivations_target_record_type",
        ),
        CheckConstraint(
            "status IN ('pending', 'completed', 'failed')",
            name="ai_derivation_results_valid_status",
        ),
    )

    target_domain: Mapped[str] = mapped_column(String(50), nullable=False)
    target_record_id: Mapped[int] = mapped_column(Integer, nullable=False)
    derivation_type: Mapped[str] = mapped_column(String(100), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=AiDerivationStatus.PENDING,
        server_default=AiDerivationStatus.PENDING,
    )
    model_name: Mapped[str | None] = mapped_column(String(100), nullable=True)
    model_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    generated_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    failed_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    content_json: Mapped[JsonValue] = mapped_column(JSON, nullable=True)
    error_message: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
