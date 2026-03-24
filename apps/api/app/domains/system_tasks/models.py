from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Any

from sqlalchemy import CheckConstraint, DateTime, Integer, JSON, String, Text, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, IdMixin


class TaskRunStatus(str, Enum):
    PENDING = "pending"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    CANCELLED = "cancelled"


class TaskTriggerSource(str, Enum):
    API = "api"
    SYSTEM = "system"
    MANUAL = "manual"


class TaskRun(IdMixin, Base):
    __tablename__ = "task_runs"
    __table_args__ = (
        CheckConstraint(
            "status IN ('pending', 'running', 'succeeded', 'failed', 'cancelled')",
            name="ck_task_runs_valid_status",
        ),
        CheckConstraint(
            "trigger_source IN ('api', 'system', 'manual')",
            name="ck_task_runs_valid_trigger_source",
        ),
        UniqueConstraint("idempotency_key", name="uq_task_runs_idempotency_key"),
    )

    task_type: Mapped[str] = mapped_column(String(100), nullable=False)
    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=TaskRunStatus.PENDING.value,
        server_default=TaskRunStatus.PENDING.value,
    )
    trigger_source: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default=TaskTriggerSource.API.value,
        server_default=TaskTriggerSource.API.value,
    )
    payload_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    result_json: Mapped[dict[str, Any] | list[Any] | str | int | float | bool | None] = (
        mapped_column(JSON, nullable=True)
    )
    error_message: Mapped[str | None] = mapped_column(Text, nullable=True)
    attempt_count: Mapped[int] = mapped_column(
        Integer(),
        nullable=False,
        default=0,
        server_default="0",
    )
    requested_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
    started_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    finished_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    idempotency_key: Mapped[str | None] = mapped_column(String(255), nullable=True)
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
