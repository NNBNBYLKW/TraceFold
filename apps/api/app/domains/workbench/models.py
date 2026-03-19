from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, ForeignKey, Integer, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, TimestampMixin


class WorkbenchTemplateType:
    BUILTIN = "builtin"
    USER = "user"


class WorkbenchShortcutTargetType:
    ROUTE = "route"
    MODULE_VIEW = "module_view"


class WorkbenchRecentActionType:
    VIEWED = "viewed"
    ACTED = "acted"


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class WorkbenchTemplate(TimestampMixin, Base):
    __tablename__ = "workbench_templates"

    template_id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    template_type: Mapped[str] = mapped_column(String(20), nullable=False)
    name: Mapped[str] = mapped_column(String(120), nullable=False)
    default_module: Mapped[str] = mapped_column(String(50), nullable=False)
    default_view_key: Mapped[str | None] = mapped_column(String(100), nullable=True)
    default_query_json: Mapped[JsonValue] = mapped_column(JSON, nullable=True)
    description: Mapped[str | None] = mapped_column(Text, nullable=True)
    scoped_shortcut_ids: Mapped[JsonValue] = mapped_column(JSON, nullable=False, default=list)
    sort_order: Mapped[int] = mapped_column(Integer, nullable=False, default=0, server_default="0")
    is_enabled: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="1",
    )


class WorkbenchShortcut(TimestampMixin, Base):
    __tablename__ = "workbench_shortcuts"

    shortcut_id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    label: Mapped[str] = mapped_column(String(120), nullable=False)
    target_type: Mapped[str] = mapped_column(String(30), nullable=False)
    target_payload_json: Mapped[JsonValue] = mapped_column(JSON, nullable=False)
    sort_order: Mapped[int] = mapped_column(Integer, nullable=False, default=0, server_default="0")
    is_enabled: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="1",
    )


class WorkbenchRecentContext(Base):
    __tablename__ = "workbench_recent_contexts"

    recent_id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    object_type: Mapped[str] = mapped_column(String(40), nullable=False)
    object_id: Mapped[str] = mapped_column(String(100), nullable=False)
    action_type: Mapped[str] = mapped_column(String(20), nullable=False)
    title_snapshot: Mapped[str] = mapped_column(String(255), nullable=False)
    route_snapshot: Mapped[str] = mapped_column(String(255), nullable=False)
    context_payload_json: Mapped[JsonValue] = mapped_column(JSON, nullable=True)
    occurred_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
    )


class WorkbenchPreference(Base):
    __tablename__ = "workbench_preferences"

    preference_id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    default_template_id: Mapped[int | None] = mapped_column(
        ForeignKey("workbench_templates.template_id"),
        nullable=True,
    )
    active_template_id: Mapped[int | None] = mapped_column(
        ForeignKey("workbench_templates.template_id"),
        nullable=True,
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(),
        nullable=False,
        server_default=func.now(),
        onupdate=func.now(),
    )
