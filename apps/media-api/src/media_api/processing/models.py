from __future__ import annotations

from datetime import datetime
from enum import StrEnum

from sqlalchemy import Boolean, Enum, ForeignKey, Integer, JSON, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column, relationship

from media_api.infrastructure.db import Base, UTCDateTime, new_uuid, utc_now
from media_api.intake.models import InboxItem


class ProcessingJobStatus(StrEnum):
    QUEUED = "queued"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"


def _job_status_type() -> Enum:
    return Enum(
        ProcessingJobStatus,
        name="processing_job_status",
        native_enum=False,
        length=32,
        create_constraint=False,
        validate_strings=True,
        values_callable=lambda values: [value.value for value in values],
    )


class ProcessingPlan(Base):
    __tablename__ = "processing_plans"
    __table_args__ = (
        UniqueConstraint("inbox_item_id", "plan_version", name="uq_processing_plan_version"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    inbox_item_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("inbox_items.id", name="fk_processing_plans_item", ondelete="CASCADE"),
        nullable=False,
    )
    plan_version: Mapped[int] = mapped_column(Integer, nullable=False)
    processor_name: Mapped[str] = mapped_column(String(150), nullable=False)
    processor_version: Mapped[str] = mapped_column(String(50), nullable=False)
    steps_json: Mapped[list[dict[str, object]]] = mapped_column(JSON, nullable=False)
    declared_inputs_json: Mapped[list[dict[str, object]]] = mapped_column(JSON, nullable=False)
    expected_output_name: Mapped[str] = mapped_column(String(255), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, nullable=False
    )

    inbox_item: Mapped[InboxItem] = relationship()


class ProcessingJob(Base):
    __tablename__ = "processing_jobs"
    __table_args__ = (
        UniqueConstraint("idempotency_key", name="uq_processing_job_idempotency"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    inbox_item_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("inbox_items.id", name="fk_processing_jobs_item", ondelete="CASCADE"),
        nullable=False,
    )
    processing_plan_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("processing_plans.id", name="fk_processing_jobs_plan"),
        nullable=False,
    )
    idempotency_key: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[ProcessingJobStatus] = mapped_column(
        _job_status_type(), default=ProcessingJobStatus.QUEUED, nullable=False
    )
    attempt_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    current_step: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    output_path: Mapped[str | None] = mapped_column(Text)
    error_code: Mapped[str | None] = mapped_column(String(100))
    user_message: Mapped[str | None] = mapped_column(Text)
    diagnostic_message: Mapped[str | None] = mapped_column(Text)
    retryable: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    started_at: Mapped[datetime | None] = mapped_column(UTCDateTime())
    finished_at: Mapped[datetime | None] = mapped_column(UTCDateTime())
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, onupdate=utc_now, nullable=False
    )

    inbox_item: Mapped[InboxItem] = relationship()
    processing_plan: Mapped[ProcessingPlan] = relationship()
