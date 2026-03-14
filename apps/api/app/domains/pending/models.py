from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import JSON, DateTime, ForeignKey, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class PendingStatus:
    OPEN = "open"
    CONFIRMED = "confirmed"
    DISCARDED = "discarded"
    FORCED = "forced"


class PendingActionType:
    CONFIRM = "confirm"
    DISCARD = "discard"
    FIX = "fix"
    FORCE_INSERT = "force_insert"


class PendingItem(IdMixin, Base):
    __tablename__ = "pending_items"

    capture_id: Mapped[int] = mapped_column(ForeignKey("capture_records.id"), nullable=False)
    parse_result_id: Mapped[int] = mapped_column(ForeignKey("parse_results.id"), nullable=False)
    target_domain: Mapped[str] = mapped_column(String(20), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=PendingStatus.OPEN,
        server_default=PendingStatus.OPEN,
    )
    proposed_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    corrected_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    reason: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)


class PendingReviewAction(IdMixin, Base):
    __tablename__ = "pending_review_actions"

    pending_item_id: Mapped[int] = mapped_column(ForeignKey("pending_items.id"), nullable=False)
    action_type: Mapped[str] = mapped_column(String(20), nullable=False)
    before_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    after_payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    note: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
