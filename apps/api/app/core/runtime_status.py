from __future__ import annotations

from datetime import UTC, datetime

from sqlalchemy.exc import OperationalError

from app.api.schemas import RuntimeStatusRead
from app.core.exceptions import DatabaseUnavailableError, MigrationStateError, TaskRuntimeUnavailableError
from app.core.logging import build_log_message, get_logger
from app.db.migrations import get_current_revision, get_head_revision
from app.db.session import SessionLocal, engine
from app.domains.ai_derivations.service import get_ai_derivation_status_summary
from app.domains.system_tasks.service import get_task_runtime_status_summary


logger = get_logger(__name__)


def get_runtime_status_read() -> RuntimeStatusRead:
    checked_at = _utcnow()
    degraded_reasons: list[str] = []
    db_status = "ok"
    migration_status = "ok"
    schema_version: str | None = None
    migration_head: str | None = None
    task_runtime_status = "degraded"

    try:
        _check_database()
    except DatabaseUnavailableError:
        db_status = "unavailable"
        migration_status = "unknown"
        degraded_reasons.append("database_unavailable")

    try:
        migration_head = get_head_revision()
    except MigrationStateError:
        migration_status = "failed"
        degraded_reasons.append("migration_head_unavailable")

    if db_status == "ok":
        try:
            schema_version = get_current_revision()
        except DatabaseUnavailableError:
            db_status = "unavailable"
            migration_status = "unknown"
            degraded_reasons.append("database_unavailable")
        except MigrationStateError:
            migration_status = "failed"
            degraded_reasons.append("migration_state_error")

    if db_status == "ok" and migration_status == "ok":
        if schema_version is None:
            migration_status = "failed"
            degraded_reasons.append("schema_not_initialized")
        elif migration_head is not None and schema_version != migration_head:
            migration_status = "outdated"
            degraded_reasons.append("migration_not_at_head")

    if db_status == "ok":
        try:
            task_summary = _get_task_runtime_summary()
            task_runtime_status = str(task_summary["task_runtime_status"])
            for reason in task_summary["degraded_reasons"]:
                if reason not in degraded_reasons:
                    degraded_reasons.append(reason)
        except TaskRuntimeUnavailableError:
            task_runtime_status = "degraded"
            degraded_reasons.append("task_runtime_unavailable")
        try:
            derivation_summary = _get_ai_derivation_summary()
            for reason in derivation_summary["degraded_reasons"]:
                if reason not in degraded_reasons:
                    degraded_reasons.append(reason)
        except TaskRuntimeUnavailableError:
            degraded_reasons.append("ai_derivation_runtime_unavailable")

    api_status = "degraded" if degraded_reasons else "ok"

    return RuntimeStatusRead(
        api_status=api_status,
        db_status=db_status,
        migration_head=migration_head,
        schema_version=schema_version,
        migration_status=migration_status,
        degraded_reasons=degraded_reasons,
        task_runtime_status=task_runtime_status,
        last_checked_at=checked_at,
    )


def _check_database() -> None:
    try:
        with engine.connect() as connection:
            connection.exec_driver_sql("SELECT 1")
    except OperationalError as exc:
        logger.exception(
            build_log_message(
                "runtime_status_database_check_failed",
                reason="database_unavailable",
            )
        )
        raise DatabaseUnavailableError(
            details={"reason": "connectivity_check_failed"},
        ) from exc
    except Exception as exc:  # pragma: no cover - unexpected driver edge
        logger.exception(
            build_log_message(
                "runtime_status_database_check_failed",
                reason="unexpected_database_check_error",
                error_class=exc.__class__.__name__,
            )
        )
        raise DatabaseUnavailableError(
            details={"reason": "unexpected_database_check_error"},
        ) from exc


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)


def _get_task_runtime_summary() -> dict[str, object]:
    db = SessionLocal()
    try:
        return get_task_runtime_status_summary(db)
    except TaskRuntimeUnavailableError:
        raise
    except Exception as exc:  # pragma: no cover - defensive integration boundary
        raise TaskRuntimeUnavailableError(
            details={"operation": "task_runtime_status_summary"},
        ) from exc
    finally:
        db.close()


def _get_ai_derivation_summary() -> dict[str, object]:
    db = SessionLocal()
    try:
        return get_ai_derivation_status_summary(db)
    except Exception as exc:  # pragma: no cover - defensive integration boundary
        raise TaskRuntimeUnavailableError(
            details={"operation": "ai_derivation_status_summary"},
        ) from exc
    finally:
        db.close()
