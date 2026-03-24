from __future__ import annotations

from datetime import UTC, datetime
from typing import Any

from sqlalchemy.orm import Session

from app.domains.system_tasks.models import TaskRun, TaskRunStatus


def create_task(
    db: Session,
    *,
    task_type: str,
    trigger_source: str,
    payload_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None,
    idempotency_key: str | None = None,
) -> TaskRun:
    task = TaskRun(
        task_type=task_type,
        status=TaskRunStatus.PENDING.value,
        trigger_source=trigger_source,
        payload_json=payload_json,
        idempotency_key=idempotency_key,
    )
    db.add(task)
    db.flush()
    return task


def get_task_by_id(db: Session, task_id: int) -> TaskRun | None:
    return db.get(TaskRun, task_id)


def list_tasks(
    db: Session,
    *,
    status: str | None = None,
    task_type: str | None = None,
    limit: int = 50,
) -> list[TaskRun]:
    query = db.query(TaskRun)
    if status is not None:
        query = query.filter(TaskRun.status == status)
    if task_type is not None:
        query = query.filter(TaskRun.task_type == task_type)
    return list(query.order_by(TaskRun.requested_at.desc(), TaskRun.id.desc()).limit(limit).all())


def count_tasks(
    db: Session,
    *,
    status: str | None = None,
    task_type: str | None = None,
) -> int:
    query = db.query(TaskRun)
    if status is not None:
        query = query.filter(TaskRun.status == status)
    if task_type is not None:
        query = query.filter(TaskRun.task_type == task_type)
    return int(query.count())


def mark_running(db: Session, task_id: int) -> TaskRun | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskRunStatus.RUNNING.value
    task.started_at = _utcnow()
    task.error_message = None
    task.attempt_count += 1
    db.flush()
    return task


def mark_succeeded(
    db: Session,
    task_id: int,
    *,
    result_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None,
) -> TaskRun | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskRunStatus.SUCCEEDED.value
    task.result_json = result_json
    task.error_message = None
    task.finished_at = _utcnow()
    db.flush()
    return task


def mark_failed(db: Session, task_id: int, *, error_message: str) -> TaskRun | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskRunStatus.FAILED.value
    task.error_message = error_message
    task.finished_at = _utcnow()
    db.flush()
    return task


def mark_cancelled(db: Session, task_id: int) -> TaskRun | None:
    task = get_task_by_id(db, task_id)
    if task is None:
        return None

    task.status = TaskRunStatus.CANCELLED.value
    task.finished_at = _utcnow()
    db.flush()
    return task


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
