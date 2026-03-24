"""Upgrade background task storage into the formal task runtime baseline."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision = "20260323_0002"
down_revision = "20260323_0001"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "task_runs",
        sa.Column("task_type", sa.String(length=100), nullable=False),
        sa.Column("status", sa.String(length=20), nullable=False, server_default="pending"),
        sa.Column("trigger_source", sa.String(length=20), nullable=False, server_default="api"),
        sa.Column("payload_json", sa.JSON(), nullable=True),
        sa.Column("result_json", sa.JSON(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column("attempt_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("requested_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("started_at", sa.DateTime(), nullable=True),
        sa.Column("finished_at", sa.DateTime(), nullable=True),
        sa.Column("idempotency_key", sa.String(length=255), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.CheckConstraint(
            "status IN ('pending', 'running', 'succeeded', 'failed', 'cancelled')",
            name="ck_task_runs_valid_status",
        ),
        sa.CheckConstraint(
            "trigger_source IN ('api', 'system')",
            name="ck_task_runs_valid_trigger_source",
        ),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_task_runs")),
        sa.UniqueConstraint("idempotency_key", name="uq_task_runs_idempotency_key"),
    )
    op.create_index(op.f("ix_task_runs_status"), "task_runs", ["status"], unique=False)
    op.create_index(op.f("ix_task_runs_task_type"), "task_runs", ["task_type"], unique=False)

    connection = op.get_bind()
    connection.execute(
        sa.text(
            """
            INSERT INTO task_runs (
                id,
                task_type,
                status,
                trigger_source,
                payload_json,
                result_json,
                error_message,
                attempt_count,
                requested_at,
                started_at,
                finished_at,
                idempotency_key,
                created_at,
                updated_at
            )
            SELECT
                id,
                task_type,
                CASE
                    WHEN status IN ('pending', 'running', 'succeeded', 'failed') THEN status
                    ELSE 'failed'
                END,
                'api',
                payload_json,
                result_json,
                error_message,
                CASE
                    WHEN status IN ('running', 'succeeded', 'failed') THEN 1
                    ELSE 0
                END,
                created_at,
                started_at,
                finished_at,
                NULL,
                created_at,
                COALESCE(finished_at, started_at, created_at)
            FROM background_tasks
            """
        )
    )

    op.drop_table("background_tasks")


def downgrade() -> None:
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

    connection = op.get_bind()
    connection.execute(
        sa.text(
            """
            INSERT INTO background_tasks (
                id,
                task_type,
                status,
                payload_json,
                result_json,
                error_message,
                created_at,
                started_at,
                finished_at
            )
            SELECT
                id,
                task_type,
                CASE
                    WHEN status = 'cancelled' THEN 'failed'
                    ELSE status
                END,
                payload_json,
                result_json,
                error_message,
                created_at,
                started_at,
                finished_at
            FROM task_runs
            """
        )
    )

    op.drop_index(op.f("ix_task_runs_task_type"), table_name="task_runs")
    op.drop_index(op.f("ix_task_runs_status"), table_name="task_runs")
    op.drop_table("task_runs")
