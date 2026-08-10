"""Persist conflict-resolution idempotency keys.

Revision ID: 20260810_0003
Revises: 20260810_0002
Create Date: 2026-08-10
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op


revision: str = "20260810_0003"
down_revision: str | None = "20260810_0002"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("ingest_operations") as batch:
        batch.add_column(
            sa.Column("resolution_idempotency_key", sa.String(length=255))
        )
        batch.create_unique_constraint(
            "uq_ingest_operation_resolution_idempotency",
            ["resolution_idempotency_key"],
        )


def downgrade() -> None:
    with op.batch_alter_table("ingest_operations") as batch:
        batch.drop_constraint(
            "uq_ingest_operation_resolution_idempotency",
            type_="unique",
        )
        batch.drop_column("resolution_idempotency_key")
