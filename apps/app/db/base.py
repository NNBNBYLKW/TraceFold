from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, MetaData, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


NAMING_CONVENTION = {
    "ix": "ix_%(column_0_label)s",
    "uq": "uq_%(table_name)s_%(column_0_name)s",
    "ck": "ck_%(table_name)s_%(constraint_name)s",
    "fk": "fk_%(table_name)s_%(column_0_name)s_%(referred_table_name)s",
    "pk": "pk_%(table_name)s",
}


metadata = MetaData(naming_convention=NAMING_CONVENTION)


class Base(DeclarativeBase):
    """
    Global declarative base for all ORM models.

    Rules:
    - All ORM models must inherit from this Base.
    - Do not create another DeclarativeBase in any domain.
    """

    metadata = metadata


class IdMixin:
    """
    Reusable integer primary key.
    """

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)


class TimestampMixin:
    """
    Reusable timestamp fields.

    Notes:
    - `created_at` is set on insert.
    - `updated_at` is set on insert and auto-updated on update.
    """

    created_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
        onupdate=func.now(),
    )


class BaseModelMixin(IdMixin, TimestampMixin):
    """
    Common base mixin for most domain models:
    - id
    - created_at
    - updated_at
    """

    pass