from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Any

from sqlalchemy import CheckConstraint, DateTime, Integer, JSON, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin, TimestampMixin


class AlertSeverity(str, Enum):
    INFO = "info"
    WARNING = "warning"
    HIGH = "high"


class AlertStatus(str, Enum):
    OPEN = "open"
    ACKNOWLEDGED = "acknowledged"
    RESOLVED = "resolved"
    INVALIDATED = "invalidated"

    # Legacy aliases kept so older imports do not silently break.
    VIEWED = "acknowledged"
    DISMISSED = "resolved"


class RuleAlert(IdMixin, TimestampMixin, Base):
    __tablename__ = "rule_alerts"
    __table_args__ = (
        UniqueConstraint(
            "domain",
            "source_record_type",
            "source_record_id",
            "rule_key",
            name="uq_rule_alerts_domain_source_record_rule",
        ),
        CheckConstraint(
            "severity IN ('info', 'warning', 'high')",
            name="rule_alerts_valid_severity",
        ),
        CheckConstraint(
            "status IN ('open', 'acknowledged', 'resolved', 'invalidated')",
            name="rule_alerts_valid_status",
        ),
    )

    domain: Mapped[str] = mapped_column(String(50), nullable=False)
    rule_key: Mapped[str] = mapped_column(String(100), nullable=False)
    severity: Mapped[str] = mapped_column(String(20), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=AlertStatus.OPEN.value,
        server_default=AlertStatus.OPEN.value,
    )
    source_record_type: Mapped[str] = mapped_column(String(50), nullable=False)
    source_record_id: Mapped[int] = mapped_column(Integer, nullable=False)
    message: Mapped[str] = mapped_column(Text, nullable=False)
    details_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    triggered_at: Mapped[datetime] = mapped_column(DateTime(), nullable=False)
    acknowledged_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    resolution_note: Mapped[str | None] = mapped_column(Text, nullable=True)

    @property
    def source_domain(self) -> str:
        return self.domain

    @property
    def rule_code(self) -> str:
        return self.rule_key

    @property
    def title(self) -> str | None:
        if isinstance(self.details_json, dict):
            title = self.details_json.get("title")
            if isinstance(title, str):
                normalized = " ".join(title.split())
                return normalized or None
        return None

    @property
    def explanation(self) -> str | None:
        if isinstance(self.details_json, dict):
            explanation = self.details_json.get("explanation")
            if isinstance(explanation, str):
                normalized = " ".join(explanation.split())
                return normalized or None
        return None

    @property
    def viewed_at(self) -> datetime | None:
        return self.acknowledged_at

    @property
    def dismissed_at(self) -> datetime | None:
        return self.resolved_at


AlertResult = RuleAlert
