from __future__ import annotations

from datetime import datetime

from sqlalchemy import CheckConstraint, DateTime, Integer, String, Text, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class AlertSeverity:
    INFO = "info"
    WARNING = "warning"
    HIGH = "high"


class AlertStatus:
    OPEN = "open"
    VIEWED = "viewed"
    DISMISSED = "dismissed"


class AlertResult(IdMixin, Base):
    __tablename__ = "alert_results"
    __table_args__ = (
        UniqueConstraint(
            "source_domain",
            "source_record_id",
            "rule_code",
            name="uq_alert_results_source_record_rule",
        ),
        CheckConstraint(
            "severity IN ('info', 'warning', 'high')",
            name="alert_results_valid_severity",
        ),
        CheckConstraint(
            "status IN ('open', 'viewed', 'dismissed')",
            name="alert_results_valid_status",
        ),
    )

    source_domain: Mapped[str] = mapped_column(String(50), nullable=False)
    source_record_id: Mapped[int] = mapped_column(Integer, nullable=False)
    rule_code: Mapped[str] = mapped_column(String(100), nullable=False)
    severity: Mapped[str] = mapped_column(String(20), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=AlertStatus.OPEN,
        server_default=AlertStatus.OPEN,
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    message: Mapped[str] = mapped_column(Text, nullable=False)
    explanation: Mapped[str | None] = mapped_column(Text, nullable=True)
    triggered_at: Mapped[datetime] = mapped_column(DateTime(), nullable=False)
    viewed_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    dismissed_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
