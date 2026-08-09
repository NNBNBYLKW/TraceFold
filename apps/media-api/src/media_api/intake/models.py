from __future__ import annotations

from datetime import datetime

from sqlalchemy import (
    Boolean,
    Column,
    DateTime,
    Enum,
    ForeignKey,
    Integer,
    String,
    Table,
    Text,
    UniqueConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from media_api.infrastructure.db import Base, new_uuid, utc_now
from media_api.intake.state import InboxStatus
from media_api.taxonomy.models import Author, Series, Tag


def _inbox_status_type() -> Enum:
    return Enum(
        InboxStatus,
        name="inbox_status",
        native_enum=False,
        length=32,
        create_constraint=False,
        validate_strings=True,
        values_callable=lambda values: [value.value for value in values],
    )


inbox_item_authors = Table(
    "inbox_item_authors",
    Base.metadata,
    Column(
        "inbox_item_id",
        String(36),
        ForeignKey("inbox_items.id", name="fk_inbox_item_authors_item", ondelete="CASCADE"),
        primary_key=True,
    ),
    Column(
        "author_id",
        String(36),
        ForeignKey("authors.id", name="fk_inbox_item_authors_author", ondelete="CASCADE"),
        primary_key=True,
    ),
    UniqueConstraint("inbox_item_id", "author_id", name="uq_inbox_author"),
)


inbox_item_tags = Table(
    "inbox_item_tags",
    Base.metadata,
    Column(
        "inbox_item_id",
        String(36),
        ForeignKey("inbox_items.id", name="fk_inbox_item_tags_item", ondelete="CASCADE"),
        primary_key=True,
    ),
    Column(
        "tag_id",
        String(36),
        ForeignKey("tags.id", name="fk_inbox_item_tags_tag", ondelete="CASCADE"),
        primary_key=True,
    ),
    UniqueConstraint("inbox_item_id", "tag_id", name="uq_inbox_tag"),
)


class ScanCandidate(Base):
    __tablename__ = "scan_candidates"
    __table_args__ = (
        UniqueConstraint("canonical_path", name="uq_scan_candidate_path"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    canonical_path: Mapped[str] = mapped_column(Text, nullable=False)
    extension: Mapped[str] = mapped_column(String(32), nullable=False)
    media_type: Mapped[str | None] = mapped_column(String(16))
    size: Mapped[int] = mapped_column(Integer, nullable=False)
    modified_ns: Mapped[int] = mapped_column(Integer, nullable=False)
    stable_observations: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    is_stable: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_claimed: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    first_seen_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, nullable=False
    )
    last_seen_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False
    )


class InboxItem(Base):
    __tablename__ = "inbox_items"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    media_type: Mapped[str] = mapped_column(String(16), nullable=False)
    status: Mapped[InboxStatus] = mapped_column(
        _inbox_status_type(), default=InboxStatus.DISCOVERED, nullable=False
    )
    suggested_actual_filename: Mapped[str] = mapped_column(String(255), nullable=False)
    display_name: Mapped[str | None] = mapped_column(String(255))
    series_id: Mapped[str | None] = mapped_column(
        String(36), ForeignKey("series.id", name="fk_inbox_items_series")
    )
    failed_from_status: Mapped[InboxStatus | None] = mapped_column(_inbox_status_type())
    error_code: Mapped[str | None] = mapped_column(String(100))
    error_message: Mapped[str | None] = mapped_column(Text)
    processing_plan_version: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    discovered_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, nullable=False
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False
    )

    sources: Mapped[list[SourceFile]] = relationship(
        back_populates="inbox_item", cascade="all, delete-orphan", lazy="selectin"
    )
    authors: Mapped[list[Author]] = relationship(
        secondary=inbox_item_authors, lazy="selectin"
    )
    series: Mapped[Series | None] = relationship(lazy="joined")
    tags: Mapped[list[Tag]] = relationship(secondary=inbox_item_tags, lazy="selectin")


class SourceFile(Base):
    __tablename__ = "source_files"
    __table_args__ = (
        UniqueConstraint("canonical_path", name="uq_source_file_path"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    inbox_item_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("inbox_items.id", name="fk_source_files_inbox_item", ondelete="CASCADE"),
        nullable=False,
    )
    canonical_path: Mapped[str] = mapped_column(Text, nullable=False)
    extension: Mapped[str] = mapped_column(String(32), nullable=False)
    size: Mapped[int] = mapped_column(Integer, nullable=False)
    modified_ns: Mapped[int] = mapped_column(Integer, nullable=False)
    scan_fingerprint: Mapped[str] = mapped_column(String(255), nullable=False)
    is_primary: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_present: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    content_sha256: Mapped[str | None] = mapped_column(String(64))
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False
    )

    inbox_item: Mapped[InboxItem] = relationship(back_populates="sources")


class InboxStatusEvent(Base):
    __tablename__ = "inbox_status_events"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    inbox_item_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey(
            "inbox_items.id", name="fk_inbox_status_events_item", ondelete="CASCADE"
        ),
        nullable=False,
    )
    from_status: Mapped[InboxStatus | None] = mapped_column(_inbox_status_type())
    to_status: Mapped[InboxStatus] = mapped_column(_inbox_status_type(), nullable=False)
    reason: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utc_now, nullable=False
    )
