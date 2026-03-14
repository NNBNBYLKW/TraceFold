from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.domains.system_tasks.models import BackgroundTask, TaskStatus


def create_task(
    db: Session,
    *,
    task_type: str,
    payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None,
) -> BackgroundTask:
    task = BackgroundTask(
        task_type=task_type,
        status=TaskStatus.PENDING.value,
        payload_json=payload_json,
    )
    db.add(task)
    db.flush()
    return task


def get_task_by_id(db: Session, task_id: int) -> BackgroundTask | None:
    return db.get(BackgroundTask, task_id)


def mark_running(db: Session, task_id: int) -> BackgroundTask | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskStatus.RUNNING.value
    task.started_at = datetime.utcnow()
    task.error_message = None
    db.flush()
    return task


def mark_succeeded(
    db: Session,
    task_id: int,
    *,
    result_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None,
) -> BackgroundTask | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskStatus.SUCCEEDED.value
    task.result_json = result_json
    task.error_message = None
    task.finished_at = datetime.utcnow()
    db.flush()
    return task


def mark_failed(db: Session, task_id: int, *, error_message: str) -> BackgroundTask | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskStatus.FAILED.value
    task.error_message = error_message
    task.finished_at = datetime.utcnow()
    db.flush()
    return task
