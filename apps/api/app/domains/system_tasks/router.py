from __future__ import annotations

import time
from typing import Any

from fastapi import APIRouter, BackgroundTasks, Depends, status
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, created_response, success_response
from app.core.tasks import submit_background_task
from app.db.session import get_db
from app.domains.system_tasks import service
from app.domains.system_tasks.schemas import TaskCreateResponse, TaskRead


router = APIRouter()


def _run_demo_task() -> dict[str, Any]:
    time.sleep(3)
    return {
        "message": "Demo task completed.",
        "sleep_seconds": 3,
        "demo_result": "ok",
    }


@router.post(
    "/demo",
    response_model=ApiResponse[TaskCreateResponse],
    status_code=status.HTTP_201_CREATED,
)
def create_demo_task(
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskCreateResponse]:
    task_id = submit_background_task(
        background_tasks,
        db,
        task_type="demo",
        task_runner=_run_demo_task,
        payload_json={"sleep_seconds": 3},
    )
    return created_response(
        data=TaskCreateResponse(task_id=task_id),
        message="System task submitted.",
    )


@router.get("/{task_id}", response_model=ApiResponse[TaskRead])
def get_system_task(
    task_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[TaskRead]:
    task = service.get_task_read(db, task_id)
    return success_response(data=task, message="System task fetched.")
