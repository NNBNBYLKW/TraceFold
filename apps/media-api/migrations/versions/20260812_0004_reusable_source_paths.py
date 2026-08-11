"""Allow a completed inbox path to be discovered again.

Revision ID: 20260812_0004
Revises: 20260810_0003
Create Date: 2026-08-12
"""

from collections.abc import Sequence

from alembic import op


revision: str = "20260812_0004"
down_revision: str | None = "20260810_0003"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("source_files") as batch:
        batch.drop_constraint("uq_source_file_path", type_="unique")
        batch.create_unique_constraint(
            "uq_source_file_item_path",
            ["inbox_item_id", "canonical_path"],
        )


def downgrade() -> None:
    with op.batch_alter_table("source_files") as batch:
        batch.drop_constraint("uq_source_file_item_path", type_="unique")
        batch.create_unique_constraint(
            "uq_source_file_path",
            ["canonical_path"],
        )
