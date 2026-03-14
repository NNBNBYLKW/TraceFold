from __future__ import annotations

import logging
from collections.abc import Callable
from typing import Any

from fastapi import BackgroundTasks
from sqlalchemy.orm import Session

from app.domains.system_tasks import service as system_tasks_service


logger = logging.getLogger(__name__)
JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def submit_background_task(
    background_tasks: BackgroundTasks,
    db: Session,
    *,
    task_type: str,
    task_runner: Callable[[], JsonValue],
    payload_json: JsonValue = None,
) -> int:
    task_id = system_tasks_service.create_task_record(
        db,
        task_type=task_type,
        payload_json=payload_json,
    )
    background_tasks.add_task(run_task_safely, task_id, task_runner)
    return task_id


def run_task_safely(task_id: int, task_runner: Callable[[], JsonValue]) -> None:
    try:
        system_tasks_service.execute_task(task_id=task_id, task_runner=task_runner)
    except Exception as exc:
        logger.exception("Unexpected background task wrapper failure.", extra={"task_id": task_id})
        try:
            system_tasks_service.force_mark_failed(
                task_id=task_id,
                error_message=f"Unhandled wrapper error: {exc}",
            )
        except Exception:
            logger.exception(
                "Failed to persist background task failure state.",
                extra={"task_id": task_id},
            )
