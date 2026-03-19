from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from app.domains.dashboard.schemas import DashboardRead


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


class WorkbenchTemplateRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    template_id: int
    template_type: str
    name: str
    default_module: str
    default_view_key: str | None = None
    default_query_json: JsonValue = None
    description: str | None = None
    scoped_shortcut_ids: list[int]
    sort_order: int
    is_enabled: bool
    created_at: datetime
    updated_at: datetime


class WorkbenchTemplateListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[WorkbenchTemplateRead]


class WorkbenchTemplateCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    template_type: str = Field(default="user")
    name: str
    default_module: str
    default_view_key: str | None = None
    default_query_json: JsonValue = None
    description: str | None = None
    scoped_shortcut_ids: list[int] = Field(default_factory=list)
    sort_order: int = 0
    is_enabled: bool = True


class WorkbenchTemplateUpdateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str | None = None
    default_module: str | None = None
    default_view_key: str | None = None
    default_query_json: JsonValue = None
    description: str | None = None
    scoped_shortcut_ids: list[int] | None = None
    sort_order: int | None = None
    is_enabled: bool | None = None


class WorkbenchTemplateApplyRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    set_as_default: bool = False


class WorkbenchApplyResultRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    template_applied: bool
    template_id: int
    active_template_id: int | None = None
    default_template_id: int | None = None


class WorkbenchShortcutRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    shortcut_id: int
    label: str
    target_type: str
    target_payload_json: JsonValue
    sort_order: int
    is_enabled: bool
    created_at: datetime
    updated_at: datetime


class WorkbenchShortcutListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[WorkbenchShortcutRead]


class WorkbenchShortcutCreateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    label: str
    target_type: str
    target_payload_json: JsonValue
    sort_order: int = 0
    is_enabled: bool = True


class WorkbenchShortcutUpdateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    label: str | None = None
    target_type: str | None = None
    target_payload_json: JsonValue = None
    sort_order: int | None = None
    is_enabled: bool | None = None


class WorkbenchRecentContextRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    recent_id: int
    object_type: str
    object_id: str
    action_type: str
    title_snapshot: str
    route_snapshot: str
    context_payload_json: JsonValue = None
    occurred_at: datetime


class WorkbenchRecentListRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[WorkbenchRecentContextRead]
    limit: int
    total: int


class WorkbenchPreferencesRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    default_template_id: int | None = None
    active_template_id: int | None = None
    updated_at: datetime


class WorkbenchPreferencesUpdateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    default_template_id: int | None = None
    active_template_id: int | None = None


class WorkbenchCurrentModeRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    template_id: int | None = None
    template_name: str | None = None
    default_module: str | None = None
    default_view_key: str | None = None
    default_query_json: JsonValue = None


class WorkbenchHomeRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    current_mode: WorkbenchCurrentModeRead
    templates: list[WorkbenchTemplateRead]
    pinned_shortcuts: list[WorkbenchShortcutRead]
    recent_contexts: list[WorkbenchRecentContextRead]
    dashboard_summary: DashboardRead
