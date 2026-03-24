"""Upgrade alert results into the formal rule alert lifecycle baseline."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision = "20260323_0004"
down_revision = "20260323_0003"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "rule_alerts",
        sa.Column("domain", sa.String(length=50), nullable=False),
        sa.Column("rule_key", sa.String(length=100), nullable=False),
        sa.Column("severity", sa.String(length=20), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="open"),
        sa.Column("source_record_type", sa.String(length=50), nullable=False),
        sa.Column("source_record_id", sa.Integer(), nullable=False),
        sa.Column("message", sa.Text(), nullable=False),
        sa.Column("details_json", sa.JSON(), nullable=True),
        sa.Column("triggered_at", sa.DateTime(), nullable=False),
        sa.Column("acknowledged_at", sa.DateTime(), nullable=True),
        sa.Column("resolved_at", sa.DateTime(), nullable=True),
        sa.Column("resolution_note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.CheckConstraint(
            "severity IN ('info', 'warning', 'high')",
            name="ck_rule_alerts_rule_alerts_valid_severity",
        ),
        sa.CheckConstraint(
            "status IN ('open', 'acknowledged', 'resolved', 'invalidated')",
            name="ck_rule_alerts_rule_alerts_valid_status",
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_rule_alerts")),
        sa.UniqueConstraint(
            "domain",
            "source_record_type",
            "source_record_id",
            "rule_key",
            name="uq_rule_alerts_domain_source_record_rule",
        ),
    )
    op.create_index(op.f("ix_rule_alerts_status"), "rule_alerts", ["status"], unique=False)
    op.create_index(op.f("ix_rule_alerts_domain"), "rule_alerts", ["domain"], unique=False)

    connection = op.get_bind()
    connection.execute(
        sa.text(
            """
            INSERT INTO rule_alerts (
                id,
                domain,
                rule_key,
                severity,
                status,
                source_record_type,
                source_record_id,
                message,
                details_json,
                triggered_at,
                acknowledged_at,
                resolved_at,
                resolution_note,
                created_at,
                updated_at
            )
            SELECT
                id,
                source_domain,
                rule_code,
                severity,
                CASE
                    WHEN status = 'viewed' THEN 'acknowledged'
                    WHEN status = 'dismissed' THEN 'resolved'
                    ELSE 'open'
                END,
                CASE
                    WHEN source_domain = 'health' THEN 'health_record'
                    WHEN source_domain = 'expense' THEN 'expense_record'
                    WHEN source_domain = 'knowledge' THEN 'knowledge_entry'
                    ELSE source_domain || '_record'
                END,
                source_record_id,
                message,
                json_object('title', title, 'explanation', explanation),
                triggered_at,
                viewed_at,
                dismissed_at,
                NULL,
                created_at,
                COALESCE(dismissed_at, viewed_at, created_at)
            FROM alert_results
            """
        )
    )

    op.drop_table("alert_results")


def downgrade() -> None:
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

    connection = op.get_bind()
    connection.execute(
        sa.text(
            """
            INSERT INTO alert_results (
                id,
                source_domain,
                source_record_id,
                rule_code,
                severity,
                status,
                title,
                message,
                explanation,
                triggered_at,
                viewed_at,
                dismissed_at,
                created_at
            )
            SELECT
                id,
                domain,
                source_record_id,
                rule_key,
                severity,
                CASE
                    WHEN status = 'acknowledged' THEN 'viewed'
                    WHEN status IN ('resolved', 'invalidated') THEN 'dismissed'
                    ELSE 'open'
                END,
                COALESCE(json_extract(details_json, '$.title'), rule_key),
                message,
                json_extract(details_json, '$.explanation'),
                triggered_at,
                acknowledged_at,
                resolved_at,
                created_at
            FROM rule_alerts
            """
        )
    )

    op.drop_index(op.f("ix_rule_alerts_domain"), table_name="rule_alerts")
    op.drop_index(op.f("ix_rule_alerts_status"), table_name="rule_alerts")
    op.drop_table("rule_alerts")
