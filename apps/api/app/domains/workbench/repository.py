from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.domains.workbench.models import (
    JsonValue,
    WorkbenchPreference,
    WorkbenchRecentContext,
    WorkbenchShortcut,
    WorkbenchTemplate,
)

_UNSET = object()


def list_templates(
    db: Session,
    *,
    include_disabled: bool = True,
) -> list[WorkbenchTemplate]:
    query = db.query(WorkbenchTemplate)
    if not include_disabled:
        query = query.filter(WorkbenchTemplate.is_enabled.is_(True))
    return query.order_by(WorkbenchTemplate.sort_order.asc(), WorkbenchTemplate.template_id.asc()).all()


def get_template_by_id(db: Session, template_id: int) -> WorkbenchTemplate | None:
    return db.get(WorkbenchTemplate, template_id)


def get_template_by_name_and_type(
    db: Session,
    *,
    name: str,
    template_type: str,
) -> WorkbenchTemplate | None:
    return (
        db.query(WorkbenchTemplate)
        .filter(
            WorkbenchTemplate.name == name,
            WorkbenchTemplate.template_type == template_type,
        )
        .first()
    )


def create_template(
    db: Session,
    *,
    template_type: str,
    name: str,
    default_module: str,
    default_view_key: str | None,
    default_query_json: JsonValue,
    description: str | None,
    scoped_shortcut_ids: JsonValue,
    sort_order: int,
    is_enabled: bool,
) -> WorkbenchTemplate:
    template = WorkbenchTemplate(
        template_type=template_type,
        name=name,
        default_module=default_module,
        default_view_key=default_view_key,
        default_query_json=default_query_json,
        description=description,
        scoped_shortcut_ids=scoped_shortcut_ids,
        sort_order=sort_order,
        is_enabled=is_enabled,
    )
    db.add(template)
    db.flush()
    return template


def update_template(
    db: Session,
    *,
    template: WorkbenchTemplate,
    values: dict[str, Any],
) -> WorkbenchTemplate:
    for key, value in values.items():
        setattr(template, key, value)
    db.flush()
    return template


def list_shortcuts(
    db: Session,
    *,
    include_disabled: bool = True,
) -> list[WorkbenchShortcut]:
    query = db.query(WorkbenchShortcut)
    if not include_disabled:
        query = query.filter(WorkbenchShortcut.is_enabled.is_(True))
    return query.order_by(WorkbenchShortcut.sort_order.asc(), WorkbenchShortcut.shortcut_id.asc()).all()


def get_shortcut_by_id(db: Session, shortcut_id: int) -> WorkbenchShortcut | None:
    return db.get(WorkbenchShortcut, shortcut_id)


def create_shortcut(
    db: Session,
    *,
    label: str,
    target_type: str,
    target_payload_json: JsonValue,
    sort_order: int,
    is_enabled: bool,
) -> WorkbenchShortcut:
    shortcut = WorkbenchShortcut(
        label=label,
        target_type=target_type,
        target_payload_json=target_payload_json,
        sort_order=sort_order,
        is_enabled=is_enabled,
    )
    db.add(shortcut)
    db.flush()
    return shortcut


def update_shortcut(
    db: Session,
    *,
    shortcut: WorkbenchShortcut,
    values: dict[str, Any],
) -> WorkbenchShortcut:
    for key, value in values.items():
        setattr(shortcut, key, value)
    db.flush()
    return shortcut


def delete_shortcut(db: Session, shortcut: WorkbenchShortcut) -> None:
    db.delete(shortcut)
    db.flush()


def list_recent_contexts(
    db: Session,
    *,
    limit: int,
) -> list[WorkbenchRecentContext]:
    return (
        db.query(WorkbenchRecentContext)
        .order_by(WorkbenchRecentContext.occurred_at.desc(), WorkbenchRecentContext.recent_id.desc())
        .limit(limit)
        .all()
    )


def count_recent_contexts(db: Session) -> int:
    return db.query(WorkbenchRecentContext).count()


def get_recent_context_by_object_action(
    db: Session,
    *,
    object_type: str,
    object_id: str,
    action_type: str,
) -> WorkbenchRecentContext | None:
    return (
        db.query(WorkbenchRecentContext)
        .filter(
            WorkbenchRecentContext.object_type == object_type,
            WorkbenchRecentContext.object_id == object_id,
            WorkbenchRecentContext.action_type == action_type,
        )
        .first()
    )


def create_recent_context(
    db: Session,
    *,
    object_type: str,
    object_id: str,
    action_type: str,
    title_snapshot: str,
    route_snapshot: str,
    context_payload_json: JsonValue,
    occurred_at: datetime,
) -> WorkbenchRecentContext:
    recent_context = WorkbenchRecentContext(
        object_type=object_type,
        object_id=object_id,
        action_type=action_type,
        title_snapshot=title_snapshot,
        route_snapshot=route_snapshot,
        context_payload_json=context_payload_json,
        occurred_at=occurred_at,
    )
    db.add(recent_context)
    db.flush()
    return recent_context


def update_recent_context(
    db: Session,
    *,
    recent_context: WorkbenchRecentContext,
    title_snapshot: str,
    route_snapshot: str,
    context_payload_json: JsonValue,
    occurred_at: datetime,
) -> WorkbenchRecentContext:
    recent_context.title_snapshot = title_snapshot
    recent_context.route_snapshot = route_snapshot
    recent_context.context_payload_json = context_payload_json
    recent_context.occurred_at = occurred_at
    db.flush()
    return recent_context


def list_recent_context_ids_to_trim(
    db: Session,
    *,
    keep_limit: int,
) -> list[int]:
    rows = (
        db.query(WorkbenchRecentContext.recent_id)
        .order_by(WorkbenchRecentContext.occurred_at.desc(), WorkbenchRecentContext.recent_id.desc())
        .offset(keep_limit)
        .all()
    )
    return [recent_id for (recent_id,) in rows]


def delete_recent_contexts_by_ids(
    db: Session,
    *,
    recent_ids: list[int],
) -> None:
    if not recent_ids:
        return
    (
        db.query(WorkbenchRecentContext)
        .filter(WorkbenchRecentContext.recent_id.in_(recent_ids))
        .delete(synchronize_session=False)
    )
    db.flush()


def get_preferences(db: Session) -> WorkbenchPreference | None:
    return (
        db.query(WorkbenchPreference)
        .order_by(WorkbenchPreference.preference_id.asc())
        .first()
    )


def create_preferences(
    db: Session,
    *,
    default_template_id: int | None,
    active_template_id: int | None,
) -> WorkbenchPreference:
    preference = WorkbenchPreference(
        default_template_id=default_template_id,
        active_template_id=active_template_id,
    )
    db.add(preference)
    db.flush()
    return preference


def update_preferences(
    db: Session,
    *,
    preference: WorkbenchPreference,
    default_template_id: int | None | Any = _UNSET,
    active_template_id: int | None | Any = _UNSET,
) -> WorkbenchPreference:
    if default_template_id is not _UNSET:
        preference.default_template_id = default_template_id
    if active_template_id is not _UNSET:
        preference.active_template_id = active_template_id
    db.flush()
    return preference
