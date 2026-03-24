"""Expand task trigger source baseline to include manual requests."""

from __future__ import annotations

from alembic import op


# revision identifiers, used by Alembic.
revision = "20260323_0003"
down_revision = "20260323_0002"
branch_labels = None
depends_on = None


def upgrade() -> None:
    with op.batch_alter_table("task_runs", recreate="always") as batch_op:
        batch_op.drop_constraint("ck_task_runs_valid_trigger_source", type_="check")
        batch_op.create_check_constraint(
            "ck_task_runs_valid_trigger_source",
            "trigger_source IN ('api', 'system', 'manual')",
        )


def downgrade() -> None:
    with op.batch_alter_table("task_runs", recreate="always") as batch_op:
        batch_op.drop_constraint("ck_task_runs_valid_trigger_source", type_="check")
        batch_op.create_check_constraint(
            "ck_task_runs_valid_trigger_source",
            "trigger_source IN ('api', 'system')",
        )
