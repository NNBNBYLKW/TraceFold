from __future__ import annotations

from datetime import datetime
from enum import StrEnum

from sqlalchemy import (
    Column,
    Enum,
    ForeignKey,
    Integer,
    JSON,
    String,
    Table,
    Text,
    UniqueConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from media_api.infrastructure.db import Base, UTCDateTime, new_uuid, utc_now
from media_api.intake.models import InboxItem
from media_api.taxonomy.models import Author, Series, Tag


class IngestOperationState(StrEnum):
    PREPARED = "prepared"
    FILE_COMMITTED = "file_committed"
    DB_COMMITTED = "db_committed"
    CLEANUP_PENDING = "cleanup_pending"
    COMPLETED = "completed"
    SKIPPED = "skipped"
    FAILED = "failed"


def _ingest_state_type() -> Enum:
    return Enum(
        IngestOperationState,
        name="ingest_operation_state",
        native_enum=False,
        length=32,
        create_constraint=False,
        validate_strings=True,
        values_callable=lambda values: [value.value for value in values],
    )


media_asset_authors = Table(
    "media_asset_authors",
    Base.metadata,
    Column(
        "media_asset_id",
        String(36),
        ForeignKey("media_assets.id", name="fk_media_asset_authors_asset", ondelete="CASCADE"),
        primary_key=True,
    ),
    Column(
        "author_id",
        String(36),
        ForeignKey("authors.id", name="fk_media_asset_authors_author", ondelete="CASCADE"),
        primary_key=True,
    ),
    UniqueConstraint("media_asset_id", "author_id", name="uq_asset_author"),
)


media_asset_tags = Table(
    "media_asset_tags",
    Base.metadata,
    Column(
        "media_asset_id",
        String(36),
        ForeignKey("media_assets.id", name="fk_media_asset_tags_asset", ondelete="CASCADE"),
        primary_key=True,
    ),
    Column(
        "tag_id",
        String(36),
        ForeignKey("tags.id", name="fk_media_asset_tags_tag", ondelete="CASCADE"),
        primary_key=True,
    ),
    UniqueConstraint("media_asset_id", "tag_id", name="uq_asset_tag"),
)


class MediaAsset(Base):
    __tablename__ = "media_assets"
    __table_args__ = (
        UniqueConstraint("final_relative_path", name="uq_media_asset_relative_path"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    media_type: Mapped[str] = mapped_column(String(16), nullable=False)
    actual_filename: Mapped[str] = mapped_column(String(255), nullable=False)
    final_relative_path: Mapped[str] = mapped_column(Text, nullable=False)
    display_name: Mapped[str | None] = mapped_column(String(255))
    series_id: Mapped[str | None] = mapped_column(
        String(36), ForeignKey("series.id", name="fk_media_assets_series")
    )
    size: Mapped[int] = mapped_column(Integer, nullable=False)
    sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    source_created_at: Mapped[datetime | None] = mapped_column(UTCDateTime())
    preview_path: Mapped[str | None] = mapped_column(Text)
    preview_sha256: Mapped[str | None] = mapped_column(String(64))
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, nullable=False
    )
    ingested_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, onupdate=utc_now, nullable=False
    )

    authors: Mapped[list[Author]] = relationship(
        secondary=media_asset_authors, lazy="selectin"
    )
    series: Mapped[Series | None] = relationship(lazy="joined")
    tags: Mapped[list[Tag]] = relationship(secondary=media_asset_tags, lazy="selectin")


class IngestOperation(Base):
    __tablename__ = "ingest_operations"
    __table_args__ = (
        UniqueConstraint("idempotency_key", name="uq_ingest_operation_idempotency"),
        UniqueConstraint(
            "resolution_idempotency_key",
            name="uq_ingest_operation_resolution_idempotency",
        ),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=new_uuid)
    inbox_item_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("inbox_items.id", name="fk_ingest_operations_item"),
        nullable=False,
    )
    asset_id: Mapped[str | None] = mapped_column(
        String(36), ForeignKey("media_assets.id", name="fk_ingest_operations_asset")
    )
    idempotency_key: Mapped[str] = mapped_column(String(255), nullable=False)
    resolution_idempotency_key: Mapped[str | None] = mapped_column(String(255))
    state: Mapped[IngestOperationState] = mapped_column(
        _ingest_state_type(), default=IngestOperationState.PREPARED, nullable=False
    )
    requested_actual_filename: Mapped[str] = mapped_column(String(255), nullable=False)
    staged_path: Mapped[str | None] = mapped_column(Text)
    final_relative_path: Mapped[str | None] = mapped_column(Text)
    source_hash: Mapped[str | None] = mapped_column(String(64))
    resolution: Mapped[str | None] = mapped_column(String(32))
    replacement_asset_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("media_assets.id", name="fk_ingest_operations_replacement_asset"),
    )
    backup_path: Mapped[str | None] = mapped_column(Text)
    conflict_kinds_json: Mapped[list[str] | None] = mapped_column(JSON)
    replacement_snapshot_json: Mapped[dict[str, object] | None] = mapped_column(JSON)
    diagnostic_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime(), default=utc_now, onupdate=utc_now, nullable=False
    )

    inbox_item: Mapped[InboxItem] = relationship()
