from __future__ import annotations

from fastapi import APIRouter, Depends, Response, status
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, created_response, success_response
from app.db.session import get_db
from app.domains.workbench import service
from app.domains.workbench.schemas import (
    WorkbenchApplyResultRead,
    WorkbenchHomeRead,
    WorkbenchPreferencesRead,
    WorkbenchPreferencesUpdateRequest,
    WorkbenchRecentListRead,
    WorkbenchShortcutCreateRequest,
    WorkbenchShortcutListRead,
    WorkbenchShortcutRead,
    WorkbenchShortcutUpdateRequest,
    WorkbenchTemplateApplyRequest,
    WorkbenchTemplateCreateRequest,
    WorkbenchTemplateListRead,
    WorkbenchTemplateRead,
    WorkbenchTemplateUpdateRequest,
)


router = APIRouter()


@router.get("/home", response_model=ApiResponse[WorkbenchHomeRead])
def get_workbench_home(
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchHomeRead]:
    result = service.get_workbench_home(db)
    return success_response(data=result, message="Workbench home fetched.")


@router.get("/templates", response_model=ApiResponse[WorkbenchTemplateListRead])
def list_templates(
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchTemplateListRead]:
    result = service.list_template_reads(db)
    return success_response(data=result, message="Workbench templates fetched.")


@router.post(
    "/templates",
    response_model=ApiResponse[WorkbenchTemplateRead],
    status_code=status.HTTP_201_CREATED,
)
def create_template(
    payload: WorkbenchTemplateCreateRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchTemplateRead]:
    result = service.create_template_read(
        db,
        template_type=payload.template_type,
        name=payload.name,
        default_module=payload.default_module,
        default_view_key=payload.default_view_key,
        default_query_json=payload.default_query_json,
        description=payload.description,
        scoped_shortcut_ids=payload.scoped_shortcut_ids,
        sort_order=payload.sort_order,
        is_enabled=payload.is_enabled,
    )
    return created_response(data=result, message="Workbench template created.")


@router.get("/templates/{template_id}", response_model=ApiResponse[WorkbenchTemplateRead])
def get_template(
    template_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchTemplateRead]:
    result = service.get_template_read(db, template_id)
    return success_response(data=result, message="Workbench template fetched.")


@router.patch("/templates/{template_id}", response_model=ApiResponse[WorkbenchTemplateRead])
def update_template(
    template_id: int,
    payload: WorkbenchTemplateUpdateRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchTemplateRead]:
    update_data = payload.model_dump(exclude_unset=True)
    result = service.update_template_read(
        db,
        template_id=template_id,
        **update_data,
    )
    return success_response(data=result, message="Workbench template updated.")


@router.post("/templates/{template_id}/apply", response_model=ApiResponse[WorkbenchApplyResultRead])
def apply_template(
    template_id: int,
    payload: WorkbenchTemplateApplyRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchApplyResultRead]:
    result = service.apply_template_read(
        db,
        template_id=template_id,
        set_as_default=payload.set_as_default,
    )
    return success_response(data=result, message="Workbench template applied.")


@router.get("/shortcuts", response_model=ApiResponse[WorkbenchShortcutListRead])
def list_shortcuts(
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchShortcutListRead]:
    result = service.list_shortcut_reads(db)
    return success_response(data=result, message="Workbench shortcuts fetched.")


@router.post(
    "/shortcuts",
    response_model=ApiResponse[WorkbenchShortcutRead],
    status_code=status.HTTP_201_CREATED,
)
def create_shortcut(
    payload: WorkbenchShortcutCreateRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchShortcutRead]:
    result = service.create_shortcut_read(
        db,
        label=payload.label,
        target_type=payload.target_type,
        target_payload_json=payload.target_payload_json,
        sort_order=payload.sort_order,
        is_enabled=payload.is_enabled,
    )
    return created_response(data=result, message="Workbench shortcut created.")


@router.patch("/shortcuts/{shortcut_id}", response_model=ApiResponse[WorkbenchShortcutRead])
def update_shortcut(
    shortcut_id: int,
    payload: WorkbenchShortcutUpdateRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchShortcutRead]:
    update_data = payload.model_dump(exclude_unset=True)
    result = service.update_shortcut_read(
        db,
        shortcut_id=shortcut_id,
        **update_data,
    )
    return success_response(data=result, message="Workbench shortcut updated.")


@router.delete("/shortcuts/{shortcut_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_shortcut(
    shortcut_id: int,
    db: Session = Depends(get_db),
) -> Response:
    service.delete_shortcut(db, shortcut_id=shortcut_id)
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.get("/recent", response_model=ApiResponse[WorkbenchRecentListRead])
def get_recent_contexts(
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchRecentListRead]:
    result = service.list_recent_reads(db)
    return success_response(data=result, message="Workbench recent contexts fetched.")


@router.get("/preferences", response_model=ApiResponse[WorkbenchPreferencesRead])
def get_preferences(
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchPreferencesRead]:
    result = service.get_preferences_read(db)
    return success_response(data=result, message="Workbench preferences fetched.")


@router.patch("/preferences", response_model=ApiResponse[WorkbenchPreferencesRead])
def update_preferences(
    payload: WorkbenchPreferencesUpdateRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[WorkbenchPreferencesRead]:
    update_data = payload.model_dump(exclude_unset=True)
    result = service.update_preferences_read(
        db,
        **update_data,
    )
    return success_response(data=result, message="Workbench preferences updated.")
