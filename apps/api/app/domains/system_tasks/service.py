from __future__ import annotations

import logging
from typing import Any

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import BadRequestError, IllegalStateError, NotFoundError, TaskRuntimeUnavailableError
from app.core.logging import get_logger, log_event
from app.db.session import SessionLocal
from app.domains.system_tasks import repository
from app.domains.system_tasks.models import TaskRun, TaskRunStatus, TaskTriggerSource
from app.domains.system_tasks.schemas import TaskListRead, TaskRead
from app.tasks.registry import ensure_task_type_supported, get_task_executor


logger = get_logger(__name__)
JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def request_task_run(
    db: Session,
    *,
    task_type: str,
    trigger_source: str = TaskTriggerSource.API.value,
    payload_json: JsonValue = None,
    idempotency_key: str | None = None,
) -> TaskRead:
    ensure_task_type_supported(task_type)
    _validate_trigger_source(trigger_source)
    task = repository.create_task(
        db,
        task_type=task_type,
        trigger_source=trigger_source,
        payload_json=payload_json,
        idempotency_key=idempotency_key,
    )
    db.commit()
    db.refresh(task)
    log_event(
        logger,
        level=logging.INFO,
        event="task_requested",
        domain="task_runtime",
        action="request",
        task_type=task.task_type,
        task_id=task.id,
        trigger_source=task.trigger_source,
        result=task.status,
    )
    return TaskRead.model_validate(task)


def list_task_reads(
    db: Session,
    *,
    status: str | None = None,
    task_type: str | None = None,
    limit: int = 50,
) -> TaskListRead:
    normalized_limit = _validate_limit(limit)
    tasks = repository.list_tasks(
        db,
        status=status,
        task_type=task_type,
        limit=normalized_limit,
    )
    total = repository.count_tasks(
        db,
        status=status,
        task_type=task_type,
    )
    return TaskListRead(
        items=[TaskRead.model_validate(task) for task in tasks],
        limit=normalized_limit,
        total=total,
    )


def get_task_read(db: Session, task_id: int) -> TaskRead:
    task = repository.get_task_by_id(db, task_id)
    if task is None:
        log_event(
            logger,
            level=logging.WARNING,
            event="task_query_failed",
            domain="task_runtime",
            action="read",
            task_id=task_id,
            result="not_found",
            error_code=ErrorCode.TASK_NOT_FOUND,
        )
        raise NotFoundError(
            message=f"Task run {task_id} was not found.",
            code=ErrorCode.TASK_NOT_FOUND,
        )

    return TaskRead.model_validate(task)


def cancel_task_run(db: Session, task_id: int) -> TaskRead:
    task = _get_task_or_raise(db, task_id)
    if task.status != TaskRunStatus.PENDING.value:
        raise IllegalStateError(
            message=f"Task run {task_id} can only be cancelled from pending state.",
            code=ErrorCode.TASK_STATE_CONFLICT,
            details={"task_id": task_id, "current_status": task.status},
        )

    task = repository.mark_cancelled(db, task_id)
    if task is None:  # pragma: no cover - defensive after prior lookup
        raise NotFoundError(
            message=f"Task run {task_id} was not found.",
            code=ErrorCode.TASK_NOT_FOUND,
        )

    db.commit()
    db.refresh(task)
    log_event(
        logger,
        level=logging.INFO,
        event="task_cancelled",
        domain="task_runtime",
        action="cancel",
        task_type=task.task_type,
        task_id=task.id,
        trigger_source=task.trigger_source,
        result=task.status,
    )
    return TaskRead.model_validate(task)


def execute_task(*, task_id: int) -> None:
    db = SessionLocal()
    try:
        execute_task_now(db, task_id=task_id)
    finally:
        db.close()


def execute_task_now(db: Session, *, task_id: int) -> None:
    try:
        task = _get_task_or_raise(db, task_id)
        if task.status == TaskRunStatus.CANCELLED.value:
            log_event(
                logger,
                level=logging.INFO,
                event="task_execution_skipped",
                domain="task_runtime",
                action="execute",
                task_type=task.task_type,
                task_id=task.id,
                trigger_source=task.trigger_source,
                result=TaskRunStatus.CANCELLED.value,
            )
            return
        if task.status != TaskRunStatus.PENDING.value:
            log_event(
                logger,
                level=logging.WARNING,
                event="task_execution_skipped",
                domain="task_runtime",
                action="execute",
                task_type=task.task_type,
                task_id=task.id,
                trigger_source=task.trigger_source,
                result=task.status,
                error_code=ErrorCode.TASK_STATE_CONFLICT,
            )
            return

        task = repository.mark_running(db, task_id)
        if task is None:
            raise NotFoundError(
                message=f"Task run {task_id} was not found.",
                code=ErrorCode.TASK_NOT_FOUND,
            )
        db.commit()
        db.refresh(task)
        log_event(
            logger,
            level=logging.INFO,
            event="task_started",
            domain="task_runtime",
            action="execute",
            task_type=task.task_type,
            task_id=task.id,
            trigger_source=task.trigger_source,
            result=task.status,
        )

        executor = get_task_executor(task.task_type)
        result = executor(db, task.payload_json, task.id)

        task = repository.mark_succeeded(db, task_id, result_json=result)
        if task is None:
            raise NotFoundError(
                message=f"Task run {task_id} was not found.",
                code=ErrorCode.TASK_NOT_FOUND,
            )
        db.commit()
        db.refresh(task)
        log_event(
            logger,
            level=logging.INFO,
            event="task_succeeded",
            domain="task_runtime",
            action="execute",
            task_type=task.task_type,
            task_id=task.id,
            trigger_source=task.trigger_source,
            result=task.status,
        )
    except Exception as exc:
        db.rollback()
        _persist_task_failure(db, task_id=task_id, exc=exc)


def force_mark_failed(*, task_id: int, error_message: str) -> None:
    db = SessionLocal()
    try:
        task = repository.mark_failed(db, task_id, error_message=error_message)
        if task is None:
            db.rollback()
            return
        db.commit()
        db.refresh(task)
        log_event(
            logger,
            level=logging.ERROR,
            event="task_failed",
            domain="task_runtime",
            action="execute",
            task_type=task.task_type,
            task_id=task.id,
            trigger_source=task.trigger_source,
            result=task.status,
            error_code=ErrorCode.INTERNAL_SERVER_ERROR,
        )
    finally:
        db.close()


def get_task_runtime_status_summary(db: Session) -> dict[str, Any]:
    try:
        failed_count = repository.count_tasks(db, status=TaskRunStatus.FAILED.value)
    except Exception as exc:  # pragma: no cover - defensive integration boundary
        raise TaskRuntimeUnavailableError(
            details={"operation": "task_runtime_status_summary"},
        ) from exc

    degraded_reasons: list[str] = []
    task_runtime_status = "ready"
    if failed_count > 0:
        task_runtime_status = "degraded"
        degraded_reasons.append("task_runs_failed_present")

    return {
        "task_runtime_status": task_runtime_status,
        "degraded_reasons": degraded_reasons,
        "failed_count": failed_count,
    }


def _persist_task_failure(db: Session, *, task_id: int, exc: Exception) -> None:
    error_code = getattr(exc, "code", ErrorCode.INTERNAL_SERVER_ERROR)
    error_message = _build_error_message(exc)
    details = getattr(exc, "details", None)

    try:
        if error_code == ErrorCode.DERIVATION_FAILED and isinstance(details, dict):
            from app.domains.ai_derivations.service import persist_derivation_failure_after_task_error

            persist_derivation_failure_after_task_error(
                db,
                target_type=str(details.get("target_type", "")),
                target_id=int(details.get("target_id", 0)),
                derivation_kind=str(details.get("derivation_kind", "")),
                error_message=_build_root_cause_message(exc),
            )
        task = repository.mark_failed(db, task_id, error_message=error_message)
        if task is None:
            db.rollback()
            return
        db.commit()
        db.refresh(task)
        log_event(
            logger,
            level=logging.ERROR,
            event="task_failed",
            domain="task_runtime",
            action="execute",
            task_type=task.task_type,
            task_id=task.id,
            trigger_source=task.trigger_source,
            result=task.status,
            error_code=error_code,
        )
    except Exception:  # pragma: no cover - fallback persistence path
        db.rollback()
        force_mark_failed(task_id=task_id, error_message=error_message)


def _build_error_message(exc: Exception) -> str:
    message = " ".join(str(exc).split())
    return message or "Task execution failed."


def _build_root_cause_message(exc: Exception) -> str:
    cause = exc.__cause__
    if cause is not None:
        message = " ".join(str(cause).split())
        if message:
            return message
    return _build_error_message(exc)


def _get_task_or_raise(db: Session, task_id: int) -> TaskRun:
    task = repository.get_task_by_id(db, task_id)
    if task is None:
        raise NotFoundError(
            message=f"Task run {task_id} was not found.",
            code=ErrorCode.TASK_NOT_FOUND,
        )
    return task


def _validate_limit(limit: int) -> int:
    if limit < 1 or limit > 100:
        raise BadRequestError(
            message="limit must be between 1 and 100.",
            code=ErrorCode.BAD_REQUEST,
            details={"limit": limit},
        )
    return limit


def _validate_trigger_source(trigger_source: str) -> str:
    allowed_values = {member.value for member in TaskTriggerSource}
    if trigger_source not in allowed_values:
        raise BadRequestError(
            message="trigger_source must be one of api, system, manual.",
            code=ErrorCode.BAD_REQUEST,
            details={
                "trigger_source": trigger_source,
                "allowed_values": sorted(allowed_values),
            },
        )
    return trigger_source
