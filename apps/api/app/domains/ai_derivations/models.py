from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import JSON, CheckConstraint, DateTime, Integer, String, Text, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class AiDerivationStatus:
    PENDING = "pending"
    RUNNING = "running"
    READY = "ready"
    FAILED = "failed"
    INVALIDATED = "invalidated"

    # Legacy alias retained for older callers/tests that still refer to the
    # original single-row "completed" state.
    COMPLETED = READY


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class AiDerivation(IdMixin, Base):
    __tablename__ = "ai_derivations"
    __table_args__ = (
        UniqueConstraint(
            "target_type",
            "target_id",
            "derivation_kind",
            name="uq_ai_derivations_target_kind",
        ),
        CheckConstraint(
            "status IN ('pending', 'running', 'ready', 'failed', 'invalidated')",
            name="ck_ai_derivations_valid_status",
        ),
    )

    target_type: Mapped[str] = mapped_column(String(50), nullable=False)
    target_id: Mapped[int] = mapped_column(Integer, nullable=False)
    derivation_kind: Mapped[str] = mapped_column(String(100), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=AiDerivationStatus.PENDING,
        server_default=AiDerivationStatus.PENDING,
    )
    model_key: Mapped[str | None] = mapped_column(String(100), nullable=True)
    model_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    source_basis_json: Mapped[JsonValue] = mapped_column(JSON, nullable=True)
    content_json: Mapped[JsonValue] = mapped_column(JSON, nullable=True)
    error_message: Mapped[str | None] = mapped_column(Text, nullable=True)
    generated_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    invalidated_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
        onupdate=func.now(),
    )

    @property
    def target_domain(self) -> str:
        return self.target_type

    @property
    def target_record_id(self) -> int:
        return self.target_id

    @property
    def derivation_type(self) -> str:
        return self.derivation_kind

    @property
    def model_name(self) -> str | None:
        return self.model_key

    @property
    def failed_at(self) -> datetime | None:
        if self.status == AiDerivationStatus.FAILED:
            return self.updated_at
        return None


AiDerivationResult = AiDerivation
