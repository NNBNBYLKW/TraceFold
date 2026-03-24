from __future__ import annotations

from fastapi import APIRouter, BackgroundTasks, Depends, status
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, created_response, success_response
from app.core.tasks import submit_background_task
from app.db.session import get_db
from app.domains.system_tasks import service
from app.domains.system_tasks.schemas import TaskCancelResponse, TaskCreateRequest, TaskCreateResponse, TaskListRead, TaskRead


router = APIRouter()


@router.post(
    "/{task_type}",
    response_model=ApiResponse[TaskCreateResponse],
    status_code=status.HTTP_201_CREATED,
)
def create_task_run(
    task_type: str,
    background_tasks: BackgroundTasks,
    payload: TaskCreateRequest | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskCreateResponse]:
    task_read = submit_background_task(
        background_tasks,
        db,
        task_type=task_type,
        payload_json=payload.payload_json if payload is not None else None,
    )
    return created_response(
        data=TaskCreateResponse(
            task_id=task_read.id,
            task_type=task_read.task_type,
            status=task_read.status,
        ),
        message="Task run submitted.",
    )


@router.get("", response_model=ApiResponse[TaskListRead])
def list_task_runs(
    status: str | None = None,
    task_type: str | None = None,
    limit: int = 50,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskListRead]:
    tasks = service.list_task_reads(
        db,
        status=status,
        task_type=task_type,
        limit=limit,
    )
    return success_response(data=tasks, message="Task runs fetched.")


@router.get("/{task_id}", response_model=ApiResponse[TaskRead])
def get_task_run(
    task_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskRead]:
    task = service.get_task_read(db, task_id)
    return success_response(data=task, message="Task run fetched.")


@router.post("/{task_id}/cancel", response_model=ApiResponse[TaskCancelResponse])
def cancel_task_run(
    task_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskCancelResponse]:
    task = service.cancel_task_run(db, task_id)
    return success_response(
        data=TaskCancelResponse(task_id=task.id, status=task.status),
        message="Task run cancelled.",
    )
