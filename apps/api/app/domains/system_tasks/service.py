from __future__ import annotations

from collections.abc import Callable
from typing import Any

from sqlalchemy.orm import Session

from app.core.exceptions import NotFoundError
from app.db.session import SessionLocal
from app.domains.system_tasks import repository
from app.domains.system_tasks.schemas import TaskRead


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def create_task_record(
    db: Session,
    *,
    task_type: str,
    payload_json: JsonValue = None,
) -> int:
    task = repository.create_task(
        db,
        task_type=task_type,
        payload_json=payload_json,
    )
    db.commit()
    return task.id


def get_task_read(db: Session, task_id: int) -> TaskRead:
    task = repository.get_task_by_id(db, task_id)
    if task is None:
        raise NotFoundError(message=f"System task {task_id} was not found.")

    return TaskRead.model_validate(task)


def execute_task(
    *,
    task_id: int,
    task_runner: Callable[[], JsonValue],
) -> None:
    db = SessionLocal()
    try:
        task = repository.mark_running(db, task_id)
        if task is None:
            raise NotFoundError(message=f"System task {task_id} was not found.")
        db.commit()

        result = task_runner()

        task = repository.mark_succeeded(db, task_id, result_json=result)
        if task is None:
            raise NotFoundError(message=f"System task {task_id} was not found.")
        db.commit()
    except Exception as exc:
        db.rollback()
        force_mark_failed(task_id=task_id, error_message=str(exc))
    finally:
        db.close()


def force_mark_failed(*, task_id: int, error_message: str) -> None:
    db = SessionLocal()
    try:
        task = repository.mark_failed(db, task_id, error_message=error_message)
        if task is not None:
            db.commit()
        else:
            db.rollback()
    finally:
        db.close()
