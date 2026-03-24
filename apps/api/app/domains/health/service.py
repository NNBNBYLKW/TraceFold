from __future__ import annotations

from datetime import datetime

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import (
    AppException,
    BadRequestError,
    DerivationFailedError,
    NotFoundError,
    RuleEvaluationFailedError,
)
from app.core.logging import build_log_message, get_logger, log_event
from app.domains.ai_derivations.schemas import AiDerivationResultListRead
from app.domains.alerts.schemas import AlertResultListRead
from app.domains.health import repository
from app.domains.health.ai_summary import rerun_health_summary_for_record, run_health_summary_on_record_create
from app.domains.health.models import HealthRecord
from app.domains.rules import service as rules_service
from app.domains.health.schemas import HealthDetailRead, HealthListItemRead, HealthListRead


_DEFAULT_SORT_BY = "created_at"
_ALLOWED_SORT_FIELDS = {"created_at", "metric_type"}
_ALLOWED_SORT_ORDERS = {"asc", "desc"}
_PREVIEW_LENGTH = 120
logger = get_logger(__name__)


def create_health_record(
    db: Session,
    *,
    source_capture_id: int,
    source_pending_id: int | None,
    payload: dict,
) -> HealthRecord:
    record = HealthRecord(
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        metric_type=str(payload.get("metric_type", "unknown")),
        value_text=payload.get("value_text"),
        note=payload.get("note"),
    )
    db.add(record)
    db.flush()
    rules_service.evaluate_health_alerts_for_record(db, health_record=record)
    run_health_summary_on_record_create(db, health_record=record)
    return record


def list_health_reads(
    db: Session,
    *,
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    metric_type: str | None = None,
    keyword: str | None = None,
) -> HealthListRead:
    validated_page, validated_page_size = _validate_pagination(page=page, page_size=page_size)
    validated_sort_by = _validate_sort_by(sort_by, default=_DEFAULT_SORT_BY)
    validated_sort_order = _validate_sort_order(sort_order)
    _validate_date_range(date_from=date_from, date_to=date_to)

    records, total = repository.list_health_records(
        db,
        page=validated_page,
        page_size=validated_page_size,
        sort_by=validated_sort_by,
        sort_order=validated_sort_order,
        date_from=date_from,
        date_to=date_to,
        metric_type=_normalize_optional_text(metric_type),
        keyword=_normalize_optional_text(keyword),
    )
    return HealthListRead(
        items=[_build_health_list_item(record) for record in records],
        page=validated_page,
        page_size=validated_page_size,
        total=total,
    )


def get_health_read(db: Session, health_id: int) -> HealthDetailRead:
    record = repository.get_health_record_by_id(db, health_id)
    if record is None:
        log_event(
            logger,
            level=30,
            event="health_read_failed",
            domain="health",
            action="read",
            target_type="health_record",
            target_id=health_id,
            result="not_found",
            error_code="HEALTH_NOT_FOUND",
        )
        raise NotFoundError(
            message=f"Health record {health_id} was not found.",
            code="HEALTH_NOT_FOUND",
        )
    return HealthDetailRead(
        id=record.id,
        created_at=record.created_at,
        metric_type=record.metric_type,
        value_text=record.value_text,
        note=record.note,
        source_capture_id=record.source_capture_id,
        source_pending_id=record.source_pending_id,
    )


def rerun_health_rules(
    db: Session,
    *,
    health_id: int,
) -> AlertResultListRead:
    try:
        record = repository.get_health_record_by_id(db, health_id)
        if record is None:
            raise NotFoundError(
                message=f"Health record {health_id} was not found.",
                code="HEALTH_NOT_FOUND",
            )

        result = rules_service.evaluate_health_alerts_for_record(db, health_record=record)
        db.commit()
        return result
    except Exception as exc:
        db.rollback()
        if isinstance(exc, AppException):
            raise
        logger.exception(
            build_log_message(
                "health_rules_rerun_failed",
                domain="health",
                health_id=health_id,
                error_code=ErrorCode.RULE_EVALUATION_FAILED,
            )
        )
        raise RuleEvaluationFailedError(
            message="Health rule rerun failed.",
            details={"target_domain": "health", "target_id": health_id},
        ) from exc


def rerun_health_summary(
    db: Session,
    *,
    health_id: int,
) -> AiDerivationResultListRead:
    try:
        record = repository.get_health_record_by_id(db, health_id)
        if record is None:
            raise NotFoundError(
                message=f"Health record {health_id} was not found.",
                code="HEALTH_NOT_FOUND",
            )

        rerun_health_summary_for_record(db, health_record=record)
        db.commit()
        return _build_ai_derivation_result_list_read(db, health_id=record.id)
    except Exception as exc:
        db.rollback()
        if isinstance(exc, AppException):
            raise
        logger.exception(
            build_log_message(
                "health_ai_summary_rerun_failed",
                domain="health",
                health_id=health_id,
                error_code=ErrorCode.DERIVATION_FAILED,
            )
        )
        raise DerivationFailedError(
            message="Health AI summary rerun failed.",
            details={"target_domain": "health", "target_id": health_id},
        ) from exc


def _build_health_list_item(record: HealthRecord) -> HealthListItemRead:
    return HealthListItemRead(
        id=record.id,
        created_at=record.created_at,
        metric_type=record.metric_type,
        value_text_preview=_build_preview(record.value_text),
        note_preview=_build_preview(record.note),
        has_source_pending=record.source_pending_id is not None,
    )


def _build_alert_result_list_read(db: Session, *, health_id: int) -> AlertResultListRead:
    from app.domains.alerts.service import list_alert_reads_for_source

    return list_alert_reads_for_source(
        db,
        domain="health",
        source_record_type="health_record",
        source_record_id=health_id,
    )


def _build_ai_derivation_result_list_read(db: Session, *, health_id: int) -> AiDerivationResultListRead:
    from app.domains.ai_derivations.service import list_ai_derivation_reads

    return list_ai_derivation_reads(
        db,
        target_domain="health",
        target_record_id=health_id,
    )


def _build_preview(value: str | None) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    if len(normalized) <= _PREVIEW_LENGTH:
        return normalized
    return normalized[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _validate_pagination(*, page: int, page_size: int) -> tuple[int, int]:
    if page < 1:
        raise BadRequestError(
            message="page must be greater than or equal to 1.",
            code="INVALID_PAGE",
        )
    if page_size < 1 or page_size > 100:
        raise BadRequestError(
            message="page_size must be between 1 and 100.",
            code="INVALID_PAGE_SIZE",
        )
    return page, page_size


def _validate_sort_by(sort_by: str | None, *, default: str) -> str:
    if sort_by is None:
        return default
    normalized_sort_by = sort_by.strip()
    if normalized_sort_by not in _ALLOWED_SORT_FIELDS:
        allowed = ", ".join(sorted(_ALLOWED_SORT_FIELDS))
        raise BadRequestError(
            message=f"sort_by must be one of: {allowed}.",
            code="INVALID_SORT_BY",
        )
    return normalized_sort_by


def _validate_sort_order(sort_order: str) -> str:
    normalized_sort_order = sort_order.strip().lower()
    if normalized_sort_order not in _ALLOWED_SORT_ORDERS:
        raise BadRequestError(
            message="sort_order must be either asc or desc.",
            code="INVALID_SORT_ORDER",
        )
    return normalized_sort_order


def _validate_date_range(*, date_from: datetime | None, date_to: datetime | None) -> None:
    if date_from is not None and date_to is not None and date_from > date_to:
        raise BadRequestError(
            message="date_from must be earlier than or equal to date_to.",
            code="INVALID_DATE_RANGE",
        )
