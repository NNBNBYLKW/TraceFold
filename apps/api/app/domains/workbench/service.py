from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError, ConflictError, NotFoundError
from app.domains.dashboard import service as dashboard_service
from app.domains.workbench import repository
from app.domains.workbench.models import (
    WorkbenchRecentActionType,
    WorkbenchShortcutTargetType,
    WorkbenchTemplate,
    WorkbenchTemplateType,
)
from app.domains.workbench.schemas import (
    WorkbenchApplyResultRead,
    WorkbenchCurrentModeRead,
    WorkbenchHomeRead,
    WorkbenchPreferencesRead,
    WorkbenchRecentContextRead,
    WorkbenchRecentListRead,
    WorkbenchShortcutRead,
    WorkbenchShortcutListRead,
    WorkbenchTemplateRead,
    WorkbenchTemplateListRead,
)


_RECENT_HOME_LIMIT = 5
_RECENT_MAX_ITEMS = 20
_UNSET = object()
_ALLOWED_TEMPLATE_TYPES = {WorkbenchTemplateType.BUILTIN, WorkbenchTemplateType.USER}
_ALLOWED_TEMPLATE_MODULES = {"dashboard", "expense", "knowledge", "health", "pending", "alerts"}
_ALLOWED_SHORTCUT_TARGET_TYPES = {
    WorkbenchShortcutTargetType.ROUTE,
    WorkbenchShortcutTargetType.MODULE_VIEW,
}
_ALLOWED_SHORTCUT_PAYLOAD_KEYS = {"route", "module", "view_key", "query"}
_QUERY_BASE_KEYS = {"page", "page_size", "sort_by", "sort_order", "date_from", "date_to"}
_QUERY_WHITELIST_BY_MODULE = {
    "dashboard": set(),
    "expense": _QUERY_BASE_KEYS | {"category", "keyword"},
    "knowledge": _QUERY_BASE_KEYS | {"keyword", "has_source_text"},
    "health": _QUERY_BASE_KEYS | {"metric_type", "keyword"},
    "pending": _QUERY_BASE_KEYS | {"status", "target_domain"},
    "alerts": {"source_domain", "source_record_id", "status"},
}
_BUILTIN_TEMPLATES = (
    {
        "template_type": WorkbenchTemplateType.BUILTIN,
        "name": "Home",
        "default_module": "dashboard",
        "default_view_key": None,
        "default_query_json": None,
        "description": "Default workbench landing mode.",
        "scoped_shortcut_ids": [],
        "sort_order": 10,
        "is_enabled": True,
    },
    {
        "template_type": WorkbenchTemplateType.BUILTIN,
        "name": "Pending Review",
        "default_module": "pending",
        "default_view_key": "list",
        "default_query_json": {"status": "open", "sort_by": "created_at", "sort_order": "desc"},
        "description": "Focus on open pending items.",
        "scoped_shortcut_ids": [],
        "sort_order": 20,
        "is_enabled": True,
    },
    {
        "template_type": WorkbenchTemplateType.BUILTIN,
        "name": "Expenses",
        "default_module": "expense",
        "default_view_key": "list",
        "default_query_json": {"sort_by": "created_at", "sort_order": "desc"},
        "description": "Continue expense work from the main workbench.",
        "scoped_shortcut_ids": [],
        "sort_order": 30,
        "is_enabled": True,
    },
)


def get_workbench_home(db: Session) -> WorkbenchHomeRead:
    templates = _ensure_templates_and_preferences(db)
    preferences = _ensure_preferences(db)
    current_template = _resolve_current_template(db, preferences.active_template_id, preferences.default_template_id)
    shortcuts = _select_home_shortcuts(db, current_template)
    recent = list_recent_reads(db, limit=_RECENT_HOME_LIMIT)
    dashboard_summary = dashboard_service.get_dashboard_read(db)

    return WorkbenchHomeRead(
        current_mode=_build_current_mode(current_template),
        templates=[_build_template_read(template) for template in templates],
        pinned_shortcuts=[_build_shortcut_read(shortcut) for shortcut in shortcuts],
        recent_contexts=recent.items,
        dashboard_summary=dashboard_summary,
    )


def list_template_reads(db: Session) -> WorkbenchTemplateListRead:
    templates = _ensure_templates_and_preferences(db)
    return WorkbenchTemplateListRead(items=[_build_template_read(template) for template in templates])


def get_template_read(db: Session, template_id: int) -> WorkbenchTemplateRead:
    _ensure_templates_and_preferences(db)
    template = _get_template_or_raise(db, template_id)
    return _build_template_read(template)


def create_template_read(
    db: Session,
    *,
    template_type: str,
    name: str,
    default_module: str,
    default_view_key: str | None,
    default_query_json: Any,
    description: str | None,
    scoped_shortcut_ids: list[int],
    sort_order: int,
    is_enabled: bool,
) -> WorkbenchTemplateRead:
    _ensure_templates_and_preferences(db)
    normalized_template_type = _validate_template_type(template_type)
    if normalized_template_type != WorkbenchTemplateType.USER:
        raise BadRequestError(
            message="Only user templates can be created through the API.",
            code="INVALID_TEMPLATE_TYPE",
        )

    validated_default_module = _validate_template_module(default_module)
    validated_query = _validate_default_query_json(
        default_module=validated_default_module,
        default_query_json=default_query_json,
    )
    validated_shortcut_ids = _validate_scoped_shortcut_ids(db, scoped_shortcut_ids)

    try:
        template = repository.create_template(
            db,
            template_type=normalized_template_type,
            name=_require_name(name),
            default_module=validated_default_module,
            default_view_key=_normalize_optional_text(default_view_key),
            default_query_json=validated_query,
            description=_normalize_optional_text(description),
            scoped_shortcut_ids=validated_shortcut_ids,
            sort_order=sort_order,
            is_enabled=is_enabled,
        )
        db.commit()
        db.refresh(template)
        return _build_template_read(template)
    except Exception:
        db.rollback()
        raise


def update_template_read(
    db: Session,
    *,
    template_id: int,
    name: str | None | object = _UNSET,
    default_module: str | None | object = _UNSET,
    default_view_key: str | None | object = _UNSET,
    default_query_json: Any = _UNSET,
    description: str | None | object = _UNSET,
    scoped_shortcut_ids: list[int] | None | object = _UNSET,
    sort_order: int | None | object = _UNSET,
    is_enabled: bool | None | object = _UNSET,
) -> WorkbenchTemplateRead:
    _ensure_templates_and_preferences(db)
    template = _get_template_or_raise(db, template_id)
    _ensure_template_is_mutable(template)

    values: dict[str, Any] = {}
    next_default_module = template.default_module
    next_default_query = template.default_query_json

    if name is not _UNSET:
        values["name"] = _require_name(name)
    if default_module is not _UNSET:
        next_default_module = _validate_template_module(default_module)
        values["default_module"] = next_default_module
    if default_view_key is not _UNSET:
        values["default_view_key"] = _normalize_optional_text(default_view_key)
    if default_query_json is not _UNSET:
        next_default_query = default_query_json
    if description is not _UNSET:
        values["description"] = _normalize_optional_text(description)
    if scoped_shortcut_ids is not _UNSET:
        values["scoped_shortcut_ids"] = _validate_scoped_shortcut_ids(db, scoped_shortcut_ids)
    if sort_order is not _UNSET:
        values["sort_order"] = sort_order
    if is_enabled is not _UNSET:
        values["is_enabled"] = is_enabled

    if default_module is not _UNSET or default_query_json is not _UNSET:
        values["default_query_json"] = _validate_default_query_json(
            default_module=next_default_module,
            default_query_json=next_default_query,
        )

    try:
        repository.update_template(db, template=template, values=values)
        db.commit()
        db.refresh(template)
        return _build_template_read(template)
    except Exception:
        db.rollback()
        raise


def apply_template_read(
    db: Session,
    *,
    template_id: int,
    set_as_default: bool = False,
) -> WorkbenchApplyResultRead:
    _ensure_templates_and_preferences(db)
    template = _get_template_or_raise(db, template_id)
    if not template.is_enabled:
        raise ConflictError(
            message=f"Template {template_id} is disabled and cannot be applied.",
            code="TEMPLATE_DISABLED",
        )

    try:
        preference = _ensure_preferences(db)
        default_template_id = preference.default_template_id
        if set_as_default or default_template_id is None:
            default_template_id = template.template_id
        repository.update_preferences(
            db,
            preference=preference,
            default_template_id=default_template_id,
            active_template_id=template.template_id,
        )
        db.commit()
        db.refresh(preference)
        return WorkbenchApplyResultRead(
            template_applied=True,
            template_id=template.template_id,
            active_template_id=preference.active_template_id,
            default_template_id=preference.default_template_id,
        )
    except Exception:
        db.rollback()
        raise


def list_shortcut_reads(db: Session) -> WorkbenchShortcutListRead:
    shortcuts = repository.list_shortcuts(db, include_disabled=True)
    return WorkbenchShortcutListRead(items=[_build_shortcut_read(shortcut) for shortcut in shortcuts])


def create_shortcut_read(
    db: Session,
    *,
    label: str,
    target_type: str,
    target_payload_json: Any,
    sort_order: int,
    is_enabled: bool,
) -> WorkbenchShortcutRead:
    validated_target_type = _validate_shortcut_target_type(target_type)
    validated_payload = _validate_shortcut_target_payload(
        target_type=validated_target_type,
        target_payload_json=target_payload_json,
    )
    try:
        shortcut = repository.create_shortcut(
            db,
            label=_require_name(label),
            target_type=validated_target_type,
            target_payload_json=validated_payload,
            sort_order=sort_order,
            is_enabled=is_enabled,
        )
        db.commit()
        db.refresh(shortcut)
        return _build_shortcut_read(shortcut)
    except Exception:
        db.rollback()
        raise


def update_shortcut_read(
    db: Session,
    *,
    shortcut_id: int,
    label: str | None | object = _UNSET,
    target_type: str | None | object = _UNSET,
    target_payload_json: Any = _UNSET,
    sort_order: int | None | object = _UNSET,
    is_enabled: bool | None | object = _UNSET,
) -> WorkbenchShortcutRead:
    shortcut = _get_shortcut_or_raise(db, shortcut_id)
    values: dict[str, Any] = {}
    next_target_type = shortcut.target_type
    next_target_payload = shortcut.target_payload_json

    if label is not _UNSET:
        values["label"] = _require_name(label)
    if target_type is not _UNSET:
        next_target_type = _validate_shortcut_target_type(target_type)
        values["target_type"] = next_target_type
    if target_payload_json is not _UNSET:
        next_target_payload = target_payload_json
    if sort_order is not _UNSET:
        values["sort_order"] = sort_order
    if is_enabled is not _UNSET:
        values["is_enabled"] = is_enabled

    if target_type is not _UNSET or target_payload_json is not _UNSET:
        values["target_payload_json"] = _validate_shortcut_target_payload(
            target_type=next_target_type,
            target_payload_json=next_target_payload,
        )

    try:
        repository.update_shortcut(db, shortcut=shortcut, values=values)
        db.commit()
        db.refresh(shortcut)
        return _build_shortcut_read(shortcut)
    except Exception:
        db.rollback()
        raise


def delete_shortcut(db: Session, *, shortcut_id: int) -> None:
    shortcut = _get_shortcut_or_raise(db, shortcut_id)
    try:
        repository.delete_shortcut(db, shortcut)
        db.commit()
    except Exception:
        db.rollback()
        raise


def list_recent_reads(db: Session, *, limit: int = _RECENT_HOME_LIMIT) -> WorkbenchRecentListRead:
    bounded_limit = min(max(limit, 1), _RECENT_HOME_LIMIT)
    items = repository.list_recent_contexts(db, limit=bounded_limit)
    total = repository.count_recent_contexts(db)
    return WorkbenchRecentListRead(
        items=[_build_recent_read(item) for item in items],
        limit=bounded_limit,
        total=total,
    )


def get_preferences_read(db: Session) -> WorkbenchPreferencesRead:
    _ensure_templates_and_preferences(db)
    preference = _ensure_preferences(db)
    return _build_preferences_read(preference)


def update_preferences_read(
    db: Session,
    *,
    default_template_id: int | None | object = _UNSET,
    active_template_id: int | None | object = _UNSET,
) -> WorkbenchPreferencesRead:
    _ensure_templates_and_preferences(db)
    if default_template_id is not _UNSET and default_template_id is not None:
        _get_template_or_raise(db, default_template_id)
    if active_template_id is not _UNSET and active_template_id is not None:
        _get_template_or_raise(db, active_template_id)

    try:
        preference = _ensure_preferences(db)
        values: dict[str, Any] = {}
        if default_template_id is not _UNSET:
            values["default_template_id"] = default_template_id
        if active_template_id is not _UNSET:
            values["active_template_id"] = active_template_id
        repository.update_preferences(db, preference=preference, **values)
        db.commit()
        db.refresh(preference)
        return _build_preferences_read(preference)
    except Exception:
        db.rollback()
        raise


def record_recent_context_best_effort(
    db: Session,
    *,
    object_type: str,
    object_id: str,
    action_type: str,
    title_snapshot: str,
    route_snapshot: str,
    context_payload_json: Any = None,
) -> None:
    try:
        _record_recent_context(
            db,
            object_type=object_type,
            object_id=object_id,
            action_type=action_type,
            title_snapshot=title_snapshot,
            route_snapshot=route_snapshot,
            context_payload_json=context_payload_json,
        )
        db.commit()
    except Exception:
        db.rollback()


def record_expense_view_best_effort(db: Session, *, expense_read: Any) -> None:
    record_recent_context_best_effort(
        db,
        object_type="expense",
        object_id=str(expense_read.id),
        action_type=WorkbenchRecentActionType.VIEWED,
        title_snapshot=_normalize_title(f"{expense_read.amount} {expense_read.currency}"),
        route_snapshot=f"/expense/{expense_read.id}",
        context_payload_json={"source_pending_id": expense_read.source_pending_id},
    )


def record_knowledge_view_best_effort(db: Session, *, knowledge_read: Any) -> None:
    title = knowledge_read.title or knowledge_read.content or knowledge_read.source_text or f"Knowledge {knowledge_read.id}"
    record_recent_context_best_effort(
        db,
        object_type="knowledge",
        object_id=str(knowledge_read.id),
        action_type=WorkbenchRecentActionType.VIEWED,
        title_snapshot=_normalize_title(title),
        route_snapshot=f"/knowledge/{knowledge_read.id}",
        context_payload_json={"source_pending_id": knowledge_read.source_pending_id},
    )


def record_health_view_best_effort(db: Session, *, health_read: Any) -> None:
    title = health_read.metric_type
    if getattr(health_read, "value_text", None):
        title = f"{health_read.metric_type}: {health_read.value_text}"
    record_recent_context_best_effort(
        db,
        object_type="health",
        object_id=str(health_read.id),
        action_type=WorkbenchRecentActionType.VIEWED,
        title_snapshot=_normalize_title(title),
        route_snapshot=f"/health/{health_read.id}",
        context_payload_json={"source_pending_id": health_read.source_pending_id},
    )


def record_pending_view_best_effort(db: Session, *, pending_read: Any) -> None:
    record_recent_context_best_effort(
        db,
        object_type="pending",
        object_id=str(pending_read.id),
        action_type=WorkbenchRecentActionType.VIEWED,
        title_snapshot=f"Pending #{pending_read.id}",
        route_snapshot=f"/pending/{pending_read.id}",
        context_payload_json={"target_domain": pending_read.target_domain, "status": pending_read.status},
    )


def record_pending_action_best_effort(db: Session, *, action_read: Any) -> None:
    record_recent_context_best_effort(
        db,
        object_type="pending",
        object_id=str(action_read.pending_id),
        action_type=WorkbenchRecentActionType.ACTED,
        title_snapshot=f"Pending #{action_read.pending_id}",
        route_snapshot=f"/pending/{action_read.pending_id}",
        context_payload_json={"target_domain": action_read.target_domain, "status": action_read.status},
    )


def _ensure_templates_and_preferences(db: Session) -> list[WorkbenchTemplate]:
    try:
        templates = repository.list_templates(db, include_disabled=True)
        if not any(template.template_type == WorkbenchTemplateType.BUILTIN for template in templates):
            for builtin in _BUILTIN_TEMPLATES:
                repository.create_template(db, **builtin)
            db.commit()
            templates = repository.list_templates(db, include_disabled=True)
        _ensure_preferences(db)
        return templates
    except Exception:
        db.rollback()
        raise


def _ensure_preferences(db: Session):
    preference = repository.get_preferences(db)
    if preference is not None:
        return preference

    templates = repository.list_templates(db, include_disabled=True)
    default_template_id = templates[0].template_id if templates else None
    preference = repository.create_preferences(
        db,
        default_template_id=default_template_id,
        active_template_id=default_template_id,
    )
    db.commit()
    db.refresh(preference)
    return preference


def _resolve_current_template(
    db: Session,
    active_template_id: int | None,
    default_template_id: int | None,
) -> WorkbenchTemplate | None:
    for template_id in (active_template_id, default_template_id):
        if template_id is None:
            continue
        template = repository.get_template_by_id(db, template_id)
        if template is not None:
            return template
    return None


def _select_home_shortcuts(
    db: Session,
    template: WorkbenchTemplate | None,
) -> list[Any]:
    enabled_shortcuts = repository.list_shortcuts(db, include_disabled=False)
    if template is None or not template.scoped_shortcut_ids:
        return enabled_shortcuts

    scoped_ids = {int(shortcut_id) for shortcut_id in template.scoped_shortcut_ids if isinstance(shortcut_id, int)}
    return [shortcut for shortcut in enabled_shortcuts if shortcut.shortcut_id in scoped_ids]


def _record_recent_context(
    db: Session,
    *,
    object_type: str,
    object_id: str,
    action_type: str,
    title_snapshot: str,
    route_snapshot: str,
    context_payload_json: Any = None,
) -> None:
    if action_type not in {WorkbenchRecentActionType.VIEWED, WorkbenchRecentActionType.ACTED}:
        raise BadRequestError(message="Unsupported recent action type.", code="INVALID_RECENT_ACTION_TYPE")

    occurred_at = datetime.now(timezone.utc).replace(tzinfo=None)
    recent_context = repository.get_recent_context_by_object_action(
        db,
        object_type=object_type,
        object_id=object_id,
        action_type=action_type,
    )
    if recent_context is None:
        repository.create_recent_context(
            db,
            object_type=object_type,
            object_id=object_id,
            action_type=action_type,
            title_snapshot=_normalize_title(title_snapshot),
            route_snapshot=route_snapshot,
            context_payload_json=context_payload_json,
            occurred_at=occurred_at,
        )
    else:
        repository.update_recent_context(
            db,
            recent_context=recent_context,
            title_snapshot=_normalize_title(title_snapshot),
            route_snapshot=route_snapshot,
            context_payload_json=context_payload_json,
            occurred_at=occurred_at,
        )

    trim_ids = repository.list_recent_context_ids_to_trim(db, keep_limit=_RECENT_MAX_ITEMS)
    repository.delete_recent_contexts_by_ids(db, recent_ids=trim_ids)


def _build_current_mode(template: WorkbenchTemplate | None) -> WorkbenchCurrentModeRead:
    if template is None:
        return WorkbenchCurrentModeRead()
    return WorkbenchCurrentModeRead(
        template_id=template.template_id,
        template_name=template.name,
        default_module=template.default_module,
        default_view_key=template.default_view_key,
        default_query_json=template.default_query_json,
    )


def _build_template_read(template: WorkbenchTemplate) -> WorkbenchTemplateRead:
    scoped_ids = template.scoped_shortcut_ids if isinstance(template.scoped_shortcut_ids, list) else []
    return WorkbenchTemplateRead(
        template_id=template.template_id,
        template_type=template.template_type,
        name=template.name,
        default_module=template.default_module,
        default_view_key=template.default_view_key,
        default_query_json=template.default_query_json,
        description=template.description,
        scoped_shortcut_ids=[int(item) for item in scoped_ids if isinstance(item, int)],
        sort_order=template.sort_order,
        is_enabled=template.is_enabled,
        created_at=template.created_at,
        updated_at=template.updated_at,
    )


def _build_shortcut_read(shortcut: Any) -> WorkbenchShortcutRead:
    return WorkbenchShortcutRead(
        shortcut_id=shortcut.shortcut_id,
        label=shortcut.label,
        target_type=shortcut.target_type,
        target_payload_json=shortcut.target_payload_json,
        sort_order=shortcut.sort_order,
        is_enabled=shortcut.is_enabled,
        created_at=shortcut.created_at,
        updated_at=shortcut.updated_at,
    )


def _build_recent_read(recent_context: Any) -> WorkbenchRecentContextRead:
    return WorkbenchRecentContextRead(
        recent_id=recent_context.recent_id,
        object_type=recent_context.object_type,
        object_id=recent_context.object_id,
        action_type=recent_context.action_type,
        title_snapshot=recent_context.title_snapshot,
        route_snapshot=recent_context.route_snapshot,
        context_payload_json=recent_context.context_payload_json,
        occurred_at=recent_context.occurred_at,
    )


def _build_preferences_read(preference: Any) -> WorkbenchPreferencesRead:
    return WorkbenchPreferencesRead(
        default_template_id=preference.default_template_id,
        active_template_id=preference.active_template_id,
        updated_at=preference.updated_at,
    )


def _get_template_or_raise(db: Session, template_id: int) -> WorkbenchTemplate:
    template = repository.get_template_by_id(db, template_id)
    if template is None:
        raise NotFoundError(
            message=f"Workbench template {template_id} was not found.",
            code="WORKBENCH_TEMPLATE_NOT_FOUND",
        )
    return template


def _get_shortcut_or_raise(db: Session, shortcut_id: int):
    shortcut = repository.get_shortcut_by_id(db, shortcut_id)
    if shortcut is None:
        raise NotFoundError(
            message=f"Workbench shortcut {shortcut_id} was not found.",
            code="WORKBENCH_SHORTCUT_NOT_FOUND",
        )
    return shortcut


def _ensure_template_is_mutable(template: WorkbenchTemplate) -> None:
    if template.template_type == WorkbenchTemplateType.BUILTIN:
        raise ConflictError(
            message="Builtin templates are read-only.",
            code="BUILTIN_TEMPLATE_READ_ONLY",
        )


def _validate_template_type(template_type: str) -> str:
    normalized = _normalize_required_text(template_type)
    if normalized not in _ALLOWED_TEMPLATE_TYPES:
        raise BadRequestError(
            message="template_type must be either builtin or user.",
            code="INVALID_TEMPLATE_TYPE",
        )
    return normalized


def _validate_template_module(default_module: str) -> str:
    normalized = _normalize_required_text(default_module)
    if normalized not in _ALLOWED_TEMPLATE_MODULES:
        allowed = ", ".join(sorted(_ALLOWED_TEMPLATE_MODULES))
        raise BadRequestError(
            message=f"default_module must be one of: {allowed}.",
            code="INVALID_TEMPLATE_MODULE",
        )
    return normalized


def _validate_default_query_json(
    *,
    default_module: str,
    default_query_json: Any,
):
    if default_query_json is None:
        return None
    if not isinstance(default_query_json, dict):
        raise BadRequestError(
            message="default_query_json must be an object.",
            code="INVALID_TEMPLATE_DEFAULT_QUERY",
        )

    allowed_keys = _QUERY_WHITELIST_BY_MODULE[default_module]
    invalid_keys = sorted(set(default_query_json.keys()) - allowed_keys)
    if invalid_keys:
        raise BadRequestError(
            message=f"default_query_json contains unsupported keys: {', '.join(invalid_keys)}.",
            code="INVALID_TEMPLATE_DEFAULT_QUERY",
        )

    for key, value in default_query_json.items():
        if isinstance(value, (dict, list)):
            raise BadRequestError(
                message=f"default_query_json key {key} must use scalar values only.",
                code="INVALID_TEMPLATE_DEFAULT_QUERY_VALUE",
            )
    return default_query_json


def _validate_scoped_shortcut_ids(db: Session, shortcut_ids: list[int]) -> list[int]:
    normalized_ids: list[int] = []
    for shortcut_id in shortcut_ids:
        if not isinstance(shortcut_id, int):
            raise BadRequestError(
                message="scoped_shortcut_ids must contain integers only.",
                code="INVALID_SCOPED_SHORTCUT_IDS",
            )
        _get_shortcut_or_raise(db, shortcut_id)
        normalized_ids.append(shortcut_id)
    return normalized_ids


def _validate_shortcut_target_type(target_type: str) -> str:
    normalized = _normalize_required_text(target_type)
    if normalized not in _ALLOWED_SHORTCUT_TARGET_TYPES:
        raise BadRequestError(
            message="target_type must be either route or module_view.",
            code="INVALID_SHORTCUT_TARGET_TYPE",
        )
    return normalized


def _validate_shortcut_target_payload(
    *,
    target_type: str,
    target_payload_json: Any,
):
    if not isinstance(target_payload_json, dict):
        raise BadRequestError(
            message="target_payload_json must be an object.",
            code="INVALID_SHORTCUT_TARGET_PAYLOAD",
        )
    invalid_keys = sorted(set(target_payload_json.keys()) - _ALLOWED_SHORTCUT_PAYLOAD_KEYS)
    if invalid_keys:
        raise BadRequestError(
            message=f"target_payload_json contains unsupported keys: {', '.join(invalid_keys)}.",
            code="INVALID_SHORTCUT_TARGET_PAYLOAD",
        )

    if target_type == WorkbenchShortcutTargetType.ROUTE:
        route_value = _normalize_optional_text(target_payload_json.get("route"))
        if route_value is None:
            raise BadRequestError(
                message="route target_type requires route in target_payload_json.",
                code="INVALID_SHORTCUT_TARGET_PAYLOAD",
            )

    if target_type == WorkbenchShortcutTargetType.MODULE_VIEW:
        module_value = target_payload_json.get("module")
        if _normalize_optional_text(module_value) is None:
            raise BadRequestError(
                message="module_view target_type requires module in target_payload_json.",
                code="INVALID_SHORTCUT_TARGET_PAYLOAD",
            )
        normalized_module = _validate_template_module(str(module_value))
        query_value = target_payload_json.get("query")
        if query_value is not None:
            _validate_default_query_json(
                default_module=normalized_module,
                default_query_json=query_value,
            )

    return target_payload_json


def _require_name(value: str) -> str:
    normalized = _normalize_required_text(value)
    if len(normalized) > 120:
        raise BadRequestError(message="name is too long.", code="INVALID_NAME")
    return normalized


def _normalize_required_text(value: Any) -> str:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        raise BadRequestError(message="A required text field is empty.", code="INVALID_TEXT")
    return normalized


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _normalize_title(value: str) -> str:
    normalized = _normalize_required_text(value)
    return normalized[:255]
