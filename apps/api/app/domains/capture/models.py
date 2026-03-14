from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import JSON, DateTime, Float, ForeignKey, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class CaptureStatus:
    RECEIVED = "received"
    PARSED = "parsed"
    PENDING = "pending"
    COMMITTED = "committed"
    DISCARDED = "discarded"
    FAILED = "failed"


class ParseTargetDomain:
    EXPENSE = "expense"
    KNOWLEDGE = "knowledge"
    HEALTH = "health"
    UNKNOWN = "unknown"


class ParseConfidenceLevel:
    HIGH = "high"
    MEDIUM = "medium"
    LOW = "low"


class CaptureRecord(IdMixin, Base):
    __tablename__ = "capture_records"

    source_type: Mapped[str] = mapped_column(String(100), nullable=False)
    source_ref: Mapped[str | None] = mapped_column(String(255), nullable=True)
    raw_text: Mapped[str | None] = mapped_column(Text, nullable=True)
    raw_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=CaptureStatus.RECEIVED,
        server_default=CaptureStatus.RECEIVED,
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
    finalized_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)


class ParseResult(IdMixin, Base):
    __tablename__ = "parse_results"

    capture_id: Mapped[int] = mapped_column(ForeignKey("capture_records.id"), nullable=False)
    target_domain: Mapped[str] = mapped_column(String(20), nullable=False)
    confidence_score: Mapped[float] = mapped_column(Float, nullable=False)
    confidence_level: Mapped[str] = mapped_column(String(20), nullable=False)
    parsed_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    parser_name: Mapped[str] = mapped_column(String(100), nullable=False)
    parser_version: Mapped[str] = mapped_column(String(50), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
