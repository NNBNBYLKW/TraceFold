from __future__ import annotations

from collections.abc import Callable
from typing import Any

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import BadRequestError
from app.tasks.executors import run_dashboard_summary_refresh, run_knowledge_summary_recompute


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None
TaskExecutor = Callable[[Session, JsonValue, int | None], JsonValue]

TASK_TYPE_DASHBOARD_SUMMARY_REFRESH = "dashboard_summary_refresh"
TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE = "knowledge_summary_recompute"

_TASK_EXECUTORS: dict[str, TaskExecutor] = {
    TASK_TYPE_DASHBOARD_SUMMARY_REFRESH: run_dashboard_summary_refresh,
    TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE: run_knowledge_summary_recompute,
}


def list_supported_task_types() -> list[str]:
    return sorted(_TASK_EXECUTORS.keys())


def ensure_task_type_supported(task_type: str) -> None:
    if task_type not in _TASK_EXECUTORS:
        raise BadRequestError(
            message=f"Task type {task_type} is not supported.",
            code=ErrorCode.INVALID_TASK_TYPE,
            details={"supported_task_types": list_supported_task_types()},
        )


def get_task_executor(task_type: str) -> TaskExecutor:
    ensure_task_type_supported(task_type)
    return _TASK_EXECUTORS[task_type]
