from __future__ import annotations

from typing import Any

from fastapi import BackgroundTasks
from sqlalchemy.orm import Session

from app.core.logging import build_log_message, get_logger
from app.domains.system_tasks import service as system_tasks_service
from app.domains.system_tasks.schemas import TaskRead


logger = get_logger(__name__)
JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def submit_background_task(
    background_tasks: BackgroundTasks,
    db: Session,
    *,
    task_type: str,
    payload_json: JsonValue = None,
) -> TaskRead:
    task_read = system_tasks_service.request_task_run(
        db,
        task_type=task_type,
        payload_json=payload_json,
    )
    background_tasks.add_task(run_task_safely, task_read.id)
    return task_read


def run_task_safely(task_id: int) -> None:
    try:
        system_tasks_service.execute_task(task_id=task_id)
    except Exception as exc:  # pragma: no cover - defensive wrapper
        logger.exception(
            build_log_message(
                "task_wrapper_failed",
                domain="task_runtime",
                action="execute",
                task_id=task_id,
                result="failed",
            )
        )
        try:
            system_tasks_service.force_mark_failed(
                task_id=task_id,
                error_message=f"Unhandled wrapper error: {exc}",
            )
        except Exception:  # pragma: no cover - defensive persistence path
            logger.exception(
                build_log_message(
                    "task_wrapper_failure_persist_failed",
                    domain="task_runtime",
                    action="execute",
                    task_id=task_id,
                    result="failed",
                )
            )
