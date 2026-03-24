"""Create the Phase 1 baseline schema through a formal Alembic revision."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision = "20260323_0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "ai_derivation_results",
        sa.Column("target_domain", sa.String(length=50), nullable=False),
        sa.Column("target_record_id", sa.Integer(), nullable=False),
        sa.Column("derivation_type", sa.String(length=100), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="pending"),
        sa.Column("model_name", sa.String(length=100), nullable=True),
        sa.Column("model_version", sa.String(length=50), nullable=True),
        sa.Column("generated_at", sa.DateTime(), nullable=True),
        sa.Column("failed_at", sa.DateTime(), nullable=True),
        sa.Column("content_json", sa.JSON(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.CheckConstraint(
            "status IN ('pending', 'completed', 'failed')",
            name=op.f("ck_ai_derivation_results_ai_derivation_results_valid_status"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_ai_derivation_results")),
        sa.UniqueConstraint(
            "target_domain",
            "target_record_id",
            "derivation_type",
            name=op.f("uq_ai_derivations_target_record_type"),
        ),
    )
    op.create_table(
        "alert_results",
        sa.Column("source_domain", sa.String(length=50), nullable=False),
        sa.Column("source_record_id", sa.Integer(), nullable=False),
        sa.Column("rule_code", sa.String(length=100), nullable=False),
        sa.Column("severity", sa.String(length=20), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="open"),
        sa.Column("title", sa.String(length=255), nullable=False),
        sa.Column("message", sa.Text(), nullable=False),
        sa.Column("explanation", sa.Text(), nullable=True),
        sa.Column("triggered_at", sa.DateTime(), nullable=False),
        sa.Column("viewed_at", sa.DateTime(), nullable=True),
        sa.Column("dismissed_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.CheckConstraint(
            "severity IN ('info', 'warning', 'high')",
            name=op.f("ck_alert_results_alert_results_valid_severity"),
        ),
        sa.CheckConstraint(
            "status IN ('open', 'viewed', 'dismissed')",
            name=op.f("ck_alert_results_alert_results_valid_status"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_alert_results")),
        sa.UniqueConstraint(
            "source_domain",
            "source_record_id",
            "rule_code",
            name=op.f("uq_alert_results_source_record_rule"),
        ),
    )
    op.create_table(
        "background_tasks",
        sa.Column("task_type", sa.String(length=100), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="pending"),
        sa.Column("payload_json", sa.JSON(), nullable=True),
        sa.Column("result_json", sa.JSON(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("started_at", sa.DateTime(), nullable=True),
        sa.Column("finished_at", sa.DateTime(), nullable=True),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_background_tasks")),
    )
    op.create_table(
        "capture_records",
        sa.Column("source_type", sa.String(length=100), nullable=False),
        sa.Column("source_ref", sa.String(length=255), nullable=True),
        sa.Column("raw_text", sa.Text(), nullable=True),
        sa.Column("raw_payload_json", sa.JSON(), nullable=True),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="received"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("finalized_at", sa.DateTime(), nullable=True),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_capture_records")),
    )
    op.create_table(
        "workbench_recent_contexts",
        sa.Column("recent_id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("object_type", sa.String(length=40), nullable=False),
        sa.Column("object_id", sa.String(length=100), nullable=False),
        sa.Column("action_type", sa.String(length=20), nullable=False),
        sa.Column("title_snapshot", sa.String(length=255), nullable=False),
        sa.Column("route_snapshot", sa.String(length=255), nullable=False),
        sa.Column("context_payload_json", sa.JSON(), nullable=True),
        sa.Column("occurred_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.PrimaryKeyConstraint("recent_id", name=op.f("pk_workbench_recent_contexts")),
    )
    op.create_table(
        "workbench_shortcuts",
        sa.Column("shortcut_id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("label", sa.String(length=120), nullable=False),
        sa.Column("target_type", sa.String(length=30), nullable=False),
        sa.Column("target_payload_json", sa.JSON(), nullable=False),
        sa.Column("sort_order", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("is_enabled", sa.Boolean(), nullable=False, server_default="1"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.PrimaryKeyConstraint("shortcut_id", name=op.f("pk_workbench_shortcuts")),
    )
    op.create_table(
        "workbench_templates",
        sa.Column("template_id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("template_type", sa.String(length=20), nullable=False),
        sa.Column("name", sa.String(length=120), nullable=False),
        sa.Column("default_module", sa.String(length=50), nullable=False),
        sa.Column("default_view_key", sa.String(length=100), nullable=True),
        sa.Column("default_query_json", sa.JSON(), nullable=True),
        sa.Column("description", sa.Text(), nullable=True),
        sa.Column("scoped_shortcut_ids", sa.JSON(), nullable=False),
        sa.Column("sort_order", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("is_enabled", sa.Boolean(), nullable=False, server_default="1"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.PrimaryKeyConstraint("template_id", name=op.f("pk_workbench_templates")),
    )
    op.create_table(
        "parse_results",
        sa.Column("capture_id", sa.Integer(), nullable=False),
        sa.Column("target_domain", sa.String(length=20), nullable=False),
        sa.Column("confidence_score", sa.Float(), nullable=False),
        sa.Column("confidence_level", sa.String(length=20), nullable=False),
        sa.Column("parsed_payload_json", sa.JSON(), nullable=True),
        sa.Column("parser_name", sa.String(length=100), nullable=False),
        sa.Column("parser_version", sa.String(length=50), nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["capture_id"],
            ["capture_records.id"],
            name=op.f("fk_parse_results_capture_id_capture_records"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_parse_results")),
    )
    op.create_table(
        "workbench_preferences",
        sa.Column("preference_id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("default_template_id", sa.Integer(), nullable=True),
        sa.Column("active_template_id", sa.Integer(), nullable=True),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.ForeignKeyConstraint(
            ["active_template_id"],
            ["workbench_templates.template_id"],
            name=op.f("fk_workbench_preferences_active_template_id_workbench_templates"),
        ),
        sa.ForeignKeyConstraint(
            ["default_template_id"],
            ["workbench_templates.template_id"],
            name=op.f("fk_workbench_preferences_default_template_id_workbench_templates"),
        ),
        sa.PrimaryKeyConstraint("preference_id", name=op.f("pk_workbench_preferences")),
    )
    op.create_table(
        "pending_items",
        sa.Column("capture_id", sa.Integer(), nullable=False),
        sa.Column("parse_result_id", sa.Integer(), nullable=False),
        sa.Column("target_domain", sa.String(length=20), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="open"),
        sa.Column("proposed_payload_json", sa.JSON(), nullable=True),
        sa.Column("corrected_payload_json", sa.JSON(), nullable=True),
        sa.Column("reason", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("resolved_at", sa.DateTime(), nullable=True),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["capture_id"],
            ["capture_records.id"],
            name=op.f("fk_pending_items_capture_id_capture_records"),
        ),
        sa.ForeignKeyConstraint(
            ["parse_result_id"],
            ["parse_results.id"],
            name=op.f("fk_pending_items_parse_result_id_parse_results"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_pending_items")),
    )
    op.create_table(
        "expense_records",
        sa.Column("source_capture_id", sa.Integer(), nullable=False),
        sa.Column("source_pending_id", sa.Integer(), nullable=True),
        sa.Column("amount", sa.String(length=50), nullable=False),
        sa.Column("currency", sa.String(length=20), nullable=False),
        sa.Column("category", sa.String(length=100), nullable=True),
        sa.Column("note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["source_capture_id"],
            ["capture_records.id"],
            name=op.f("fk_expense_records_source_capture_id_capture_records"),
        ),
        sa.ForeignKeyConstraint(
            ["source_pending_id"],
            ["pending_items.id"],
            name=op.f("fk_expense_records_source_pending_id_pending_items"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_expense_records")),
    )
    op.create_table(
        "health_records",
        sa.Column("source_capture_id", sa.Integer(), nullable=False),
        sa.Column("source_pending_id", sa.Integer(), nullable=True),
        sa.Column("metric_type", sa.String(length=100), nullable=False),
        sa.Column("value_text", sa.String(length=255), nullable=True),
        sa.Column("note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["source_capture_id"],
            ["capture_records.id"],
            name=op.f("fk_health_records_source_capture_id_capture_records"),
        ),
        sa.ForeignKeyConstraint(
            ["source_pending_id"],
            ["pending_items.id"],
            name=op.f("fk_health_records_source_pending_id_pending_items"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_health_records")),
    )
    op.create_table(
        "knowledge_entries",
        sa.Column("source_capture_id", sa.Integer(), nullable=False),
        sa.Column("source_pending_id", sa.Integer(), nullable=True),
        sa.Column("title", sa.String(length=255), nullable=True),
        sa.Column("content", sa.Text(), nullable=True),
        sa.Column("source_text", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["source_capture_id"],
            ["capture_records.id"],
            name=op.f("fk_knowledge_entries_source_capture_id_capture_records"),
        ),
        sa.ForeignKeyConstraint(
            ["source_pending_id"],
            ["pending_items.id"],
            name=op.f("fk_knowledge_entries_source_pending_id_pending_items"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_knowledge_entries")),
    )
    op.create_table(
        "pending_review_actions",
        sa.Column("pending_item_id", sa.Integer(), nullable=False),
        sa.Column("action_type", sa.String(length=20), nullable=False),
        sa.Column("before_payload_json", sa.JSON(), nullable=True),
        sa.Column("after_payload_json", sa.JSON(), nullable=True),
        sa.Column("note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.ForeignKeyConstraint(
            ["pending_item_id"],
            ["pending_items.id"],
            name=op.f("fk_pending_review_actions_pending_item_id_pending_items"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_pending_review_actions")),
    )


def downgrade() -> None:
    op.drop_table("pending_review_actions")
    op.drop_table("knowledge_entries")
    op.drop_table("health_records")
    op.drop_table("expense_records")
    op.drop_table("pending_items")
    op.drop_table("workbench_preferences")
    op.drop_table("parse_results")
    op.drop_table("workbench_templates")
    op.drop_table("workbench_shortcuts")
    op.drop_table("workbench_recent_contexts")
    op.drop_table("capture_records")
    op.drop_table("background_tasks")
    op.drop_table("alert_results")
    op.drop_table("ai_derivation_results")
