"""Create the isolated media library schema.

Revision ID: 20260809_0001
Revises:
Create Date: 2026-08-09
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op


revision: str = "20260809_0001"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "authors",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("normalized_name", sa.String(length=255), nullable=False),
        sa.Column("display_name", sa.String(length=255), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("id", name="pk_authors"),
        sa.UniqueConstraint("normalized_name", name="uq_author_normalized_name"),
    )
    op.create_table(
        "series",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("normalized_name", sa.String(length=255), nullable=False),
        sa.Column("display_name", sa.String(length=255), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("id", name="pk_series"),
        sa.UniqueConstraint("normalized_name", name="uq_series_normalized_name"),
    )
    op.create_table(
        "tags",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("normalized_name", sa.String(length=255), nullable=False),
        sa.Column("display_name", sa.String(length=255), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("id", name="pk_tags"),
        sa.UniqueConstraint("normalized_name", name="uq_tag_normalized_name"),
    )
    op.create_table(
        "scan_candidates",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("canonical_path", sa.Text(), nullable=False),
        sa.Column("extension", sa.String(length=32), nullable=False),
        sa.Column("media_type", sa.String(length=16), nullable=True),
        sa.Column("size", sa.Integer(), nullable=False),
        sa.Column("modified_ns", sa.Integer(), nullable=False),
        sa.Column("stable_observations", sa.Integer(), server_default=sa.text("1"), nullable=False),
        sa.Column("is_stable", sa.Boolean(), server_default=sa.text("0"), nullable=False),
        sa.Column("is_claimed", sa.Boolean(), server_default=sa.text("0"), nullable=False),
        sa.Column("first_seen_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("last_seen_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("id", name="pk_scan_candidates"),
        sa.UniqueConstraint("canonical_path", name="uq_scan_candidate_path"),
    )
    op.create_table(
        "inbox_items",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("media_type", sa.String(length=16), nullable=False),
        sa.Column("status", sa.String(length=32), server_default="discovered", nullable=False),
        sa.Column("suggested_actual_filename", sa.String(length=255), nullable=False),
        sa.Column("display_name", sa.String(length=255), nullable=True),
        sa.Column("series_id", sa.String(length=36), nullable=True),
        sa.Column("failed_from_status", sa.String(length=32), nullable=True),
        sa.Column("error_code", sa.String(length=100), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column("processing_plan_version", sa.Integer(), server_default=sa.text("0"), nullable=False),
        sa.Column("discovered_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["series_id"], ["series.id"], name="fk_inbox_items_series"),
        sa.PrimaryKeyConstraint("id", name="pk_inbox_items"),
    )
    op.create_table(
        "inbox_item_authors",
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("author_id", sa.String(length=36), nullable=False),
        sa.ForeignKeyConstraint(["author_id"], ["authors.id"], name="fk_inbox_item_authors_author", ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_inbox_item_authors_item", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("inbox_item_id", "author_id", name="pk_inbox_item_authors"),
        sa.UniqueConstraint("inbox_item_id", "author_id", name="uq_inbox_author"),
    )
    op.create_table(
        "inbox_item_tags",
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("tag_id", sa.String(length=36), nullable=False),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_inbox_item_tags_item", ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["tag_id"], ["tags.id"], name="fk_inbox_item_tags_tag", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("inbox_item_id", "tag_id", name="pk_inbox_item_tags"),
        sa.UniqueConstraint("inbox_item_id", "tag_id", name="uq_inbox_tag"),
    )
    op.create_table(
        "source_files",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("canonical_path", sa.Text(), nullable=False),
        sa.Column("extension", sa.String(length=32), nullable=False),
        sa.Column("size", sa.Integer(), nullable=False),
        sa.Column("modified_ns", sa.Integer(), nullable=False),
        sa.Column("scan_fingerprint", sa.String(length=255), nullable=False),
        sa.Column("is_primary", sa.Boolean(), server_default=sa.text("0"), nullable=False),
        sa.Column("is_present", sa.Boolean(), server_default=sa.text("1"), nullable=False),
        sa.Column("content_sha256", sa.String(length=64), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_source_files_inbox_item", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id", name="pk_source_files"),
        sa.UniqueConstraint("canonical_path", name="uq_source_file_path"),
    )
    op.create_table(
        "inbox_status_events",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("from_status", sa.String(length=32), nullable=True),
        sa.Column("to_status", sa.String(length=32), nullable=False),
        sa.Column("reason", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_inbox_status_events_item", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id", name="pk_inbox_status_events"),
    )
    op.create_table(
        "processing_plans",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("plan_version", sa.Integer(), nullable=False),
        sa.Column("processor_name", sa.String(length=150), nullable=False),
        sa.Column("processor_version", sa.String(length=50), nullable=False),
        sa.Column("steps_json", sa.JSON(), nullable=False),
        sa.Column("declared_inputs_json", sa.JSON(), nullable=False),
        sa.Column("expected_output_name", sa.String(length=255), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_processing_plans_item", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id", name="pk_processing_plans"),
        sa.UniqueConstraint("inbox_item_id", "plan_version", name="uq_processing_plan_version"),
    )
    op.create_table(
        "processing_jobs",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("processing_plan_id", sa.String(length=36), nullable=False),
        sa.Column("idempotency_key", sa.String(length=255), nullable=False),
        sa.Column("status", sa.String(length=32), server_default="queued", nullable=False),
        sa.Column("attempt_count", sa.Integer(), server_default=sa.text("0"), nullable=False),
        sa.Column("current_step", sa.Integer(), server_default=sa.text("0"), nullable=False),
        sa.Column("output_path", sa.Text(), nullable=True),
        sa.Column("error_code", sa.String(length=100), nullable=True),
        sa.Column("user_message", sa.Text(), nullable=True),
        sa.Column("diagnostic_message", sa.Text(), nullable=True),
        sa.Column("retryable", sa.Boolean(), server_default=sa.text("1"), nullable=False),
        sa.Column("started_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("finished_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_processing_jobs_item", ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["processing_plan_id"], ["processing_plans.id"], name="fk_processing_jobs_plan"),
        sa.PrimaryKeyConstraint("id", name="pk_processing_jobs"),
        sa.UniqueConstraint("idempotency_key", name="uq_processing_job_idempotency"),
    )
    op.create_table(
        "media_assets",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("media_type", sa.String(length=16), nullable=False),
        sa.Column("actual_filename", sa.String(length=255), nullable=False),
        sa.Column("final_relative_path", sa.Text(), nullable=False),
        sa.Column("display_name", sa.String(length=255), nullable=True),
        sa.Column("series_id", sa.String(length=36), nullable=True),
        sa.Column("size", sa.Integer(), nullable=False),
        sa.Column("sha256", sa.String(length=64), nullable=False),
        sa.Column("source_created_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("preview_path", sa.Text(), nullable=True),
        sa.Column("preview_sha256", sa.String(length=64), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("ingested_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["series_id"], ["series.id"], name="fk_media_assets_series"),
        sa.PrimaryKeyConstraint("id", name="pk_media_assets"),
        sa.UniqueConstraint("final_relative_path", name="uq_media_asset_relative_path"),
    )
    op.create_table(
        "media_asset_authors",
        sa.Column("media_asset_id", sa.String(length=36), nullable=False),
        sa.Column("author_id", sa.String(length=36), nullable=False),
        sa.ForeignKeyConstraint(["author_id"], ["authors.id"], name="fk_media_asset_authors_author", ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["media_asset_id"], ["media_assets.id"], name="fk_media_asset_authors_asset", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("media_asset_id", "author_id", name="pk_media_asset_authors"),
        sa.UniqueConstraint("media_asset_id", "author_id", name="uq_asset_author"),
    )
    op.create_table(
        "media_asset_tags",
        sa.Column("media_asset_id", sa.String(length=36), nullable=False),
        sa.Column("tag_id", sa.String(length=36), nullable=False),
        sa.ForeignKeyConstraint(["media_asset_id"], ["media_assets.id"], name="fk_media_asset_tags_asset", ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["tag_id"], ["tags.id"], name="fk_media_asset_tags_tag", ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("media_asset_id", "tag_id", name="pk_media_asset_tags"),
        sa.UniqueConstraint("media_asset_id", "tag_id", name="uq_asset_tag"),
    )
    op.create_table(
        "ingest_operations",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("inbox_item_id", sa.String(length=36), nullable=False),
        sa.Column("asset_id", sa.String(length=36), nullable=True),
        sa.Column("idempotency_key", sa.String(length=255), nullable=False),
        sa.Column("state", sa.String(length=32), server_default="prepared", nullable=False),
        sa.Column("requested_actual_filename", sa.String(length=255), nullable=False),
        sa.Column("staged_path", sa.Text(), nullable=True),
        sa.Column("final_relative_path", sa.Text(), nullable=True),
        sa.Column("source_hash", sa.String(length=64), nullable=True),
        sa.Column("resolution", sa.String(length=32), nullable=True),
        sa.Column("replacement_asset_id", sa.String(length=36), nullable=True),
        sa.Column("backup_path", sa.Text(), nullable=True),
        sa.Column("conflict_kinds_json", sa.JSON(), nullable=True),
        sa.Column("replacement_snapshot_json", sa.JSON(), nullable=True),
        sa.Column("diagnostic_message", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["asset_id"], ["media_assets.id"], name="fk_ingest_operations_asset"),
        sa.ForeignKeyConstraint(["inbox_item_id"], ["inbox_items.id"], name="fk_ingest_operations_item"),
        sa.ForeignKeyConstraint(["replacement_asset_id"], ["media_assets.id"], name="fk_ingest_operations_replacement_asset"),
        sa.PrimaryKeyConstraint("id", name="pk_ingest_operations"),
        sa.UniqueConstraint("idempotency_key", name="uq_ingest_operation_idempotency"),
    )
    op.create_table(
        "app_settings",
        sa.Column("key", sa.String(length=150), nullable=False),
        sa.Column("value_json", sa.JSON(), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("key", name="pk_app_settings"),
    )
    op.create_table(
        "processor_settings",
        sa.Column("processor_name", sa.String(length=150), nullable=False),
        sa.Column("enabled", sa.Boolean(), server_default=sa.text("1"), nullable=False),
        sa.Column("auto_run_default", sa.Boolean(), server_default=sa.text("1"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("processor_name", name="pk_processor_settings"),
    )


def downgrade() -> None:
    op.drop_table("processor_settings")
    op.drop_table("app_settings")
    op.drop_table("ingest_operations")
    op.drop_table("media_asset_tags")
    op.drop_table("media_asset_authors")
    op.drop_table("media_assets")
    op.drop_table("processing_jobs")
    op.drop_table("processing_plans")
    op.drop_table("inbox_status_events")
    op.drop_table("source_files")
    op.drop_table("inbox_item_tags")
    op.drop_table("inbox_item_authors")
    op.drop_table("inbox_items")
    op.drop_table("scan_candidates")
    op.drop_table("tags")
    op.drop_table("series")
    op.drop_table("authors")
