"""Formalize AI derivation runtime lifecycle baseline.

Revision ID: 20260323_0005
Revises: 20260323_0004
Create Date: 2026-03-23
"""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision = "20260323_0005"
down_revision = "20260323_0004"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "ai_derivations",
        sa.Column("target_type", sa.String(length=50), nullable=False),
        sa.Column("target_id", sa.Integer(), nullable=False),
        sa.Column("derivation_kind", sa.String(length=100), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="pending"),
        sa.Column("model_key", sa.String(length=100), nullable=True),
        sa.Column("model_version", sa.String(length=50), nullable=True),
        sa.Column("source_basis_json", sa.JSON(), nullable=True),
        sa.Column("content_json", sa.JSON(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column("generated_at", sa.DateTime(), nullable=True),
        sa.Column("invalidated_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.CheckConstraint(
            "status IN ('pending', 'running', 'ready', 'failed', 'invalidated')",
            name=op.f("ck_ai_derivations_valid_status"),
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_ai_derivations")),
        sa.UniqueConstraint(
            "target_type",
            "target_id",
            "derivation_kind",
            name=op.f("uq_ai_derivations_target_kind"),
        ),
    )
    op.create_index(op.f("ix_ai_derivations_target"), "ai_derivations", ["target_type", "target_id"], unique=False)
    op.create_index(op.f("ix_ai_derivations_status"), "ai_derivations", ["status"], unique=False)

    op.execute(
        """
        INSERT INTO ai_derivations (
            target_type,
            target_id,
            derivation_kind,
            status,
            model_key,
            model_version,
            source_basis_json,
            content_json,
            error_message,
            generated_at,
            invalidated_at,
            created_at,
            updated_at,
            id
        )
        SELECT
            target_domain,
            target_record_id,
            derivation_type,
            CASE status
                WHEN 'completed' THEN 'ready'
                ELSE status
            END,
            model_name,
            model_version,
            json_object(
                'target_type', target_domain,
                'target_id', target_record_id,
                'migrated_from', 'ai_derivation_results'
            ),
            content_json,
            error_message,
            generated_at,
            NULL,
            created_at,
            coalesce(failed_at, generated_at, created_at),
            id
        FROM ai_derivation_results
        """
    )

    op.drop_table("ai_derivation_results")


def downgrade() -> None:
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

    op.execute(
        """
        INSERT INTO ai_derivation_results (
            target_domain,
            target_record_id,
            derivation_type,
            status,
            model_name,
            model_version,
            generated_at,
            failed_at,
            content_json,
            error_message,
            created_at,
            id
        )
        SELECT
            target_type,
            target_id,
            derivation_kind,
            CASE status
                WHEN 'ready' THEN 'completed'
                WHEN 'running' THEN 'pending'
                WHEN 'invalidated' THEN 'failed'
                ELSE status
            END,
            model_key,
            model_version,
            generated_at,
            CASE
                WHEN status = 'failed' THEN updated_at
                ELSE NULL
            END,
            content_json,
            error_message,
            created_at,
            id
        FROM ai_derivations
        """
    )

    op.drop_index(op.f("ix_ai_derivations_status"), table_name="ai_derivations")
    op.drop_index(op.f("ix_ai_derivations_target"), table_name="ai_derivations")
    op.drop_table("ai_derivations")
