"""Persist processing output fingerprints.

Revision ID: 20260810_0002
Revises: 20260809_0001
Create Date: 2026-08-10
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op


revision: str = "20260810_0002"
down_revision: str | None = "20260809_0001"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column("processing_jobs", sa.Column("output_size", sa.Integer()))
    op.add_column("processing_jobs", sa.Column("output_modified_ns", sa.Integer()))
    op.add_column(
        "processing_jobs",
        sa.Column("output_sha256", sa.String(length=64)),
    )


def downgrade() -> None:
    op.drop_column("processing_jobs", "output_sha256")
    op.drop_column("processing_jobs", "output_modified_ns")
    op.drop_column("processing_jobs", "output_size")
