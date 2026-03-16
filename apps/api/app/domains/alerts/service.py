from __future__ import annotations

from datetime import UTC, datetime

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError, ConflictError, NotFoundError
from app.domains.alerts import repository
from app.domains.alerts.models import AlertResult, AlertSeverity, AlertStatus
from app.domains.alerts.schemas import AlertResultListRead, AlertResultRead


_ALLOWED_ALERT_SEVERITIES = {
    AlertSeverity.INFO,
    AlertSeverity.WARNING,
    AlertSeverity.HIGH,
}
_ALLOWED_ALERT_STATUSES = {
    AlertStatus.OPEN,
    AlertStatus.VIEWED,
    AlertStatus.DISMISSED,
}


def upsert_alert_result(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    rule_code: str,
    severity: str,
    status: str = AlertStatus.OPEN,
    title: str,
    message: str,
    explanation: str | None = None,
    triggered_at: datetime,
    viewed_at: datetime | None = None,
    dismissed_at: datetime | None = None,
) -> AlertResult:
    normalized_source_domain = _normalize_required_text(
        source_domain,
        field_name="source_domain",
        code="INVALID_ALERT_SOURCE_DOMAIN",
    )
    validated_source_record_id = _validate_record_id(
        source_record_id,
        field_name="source_record_id",
        code="INVALID_ALERT_SOURCE_RECORD_ID",
    )
    normalized_rule_code = _normalize_required_text(
        rule_code,
        field_name="rule_code",
        code="INVALID_ALERT_RULE_CODE",
    )
    validated_severity = _validate_alert_severity(severity)
    validated_status = _validate_alert_status(status)
    normalized_title = _normalize_required_text(
        title,
        field_name="title",
        code="INVALID_ALERT_TITLE",
    )
    normalized_message = _normalize_required_text(
        message,
        field_name="message",
        code="INVALID_ALERT_MESSAGE",
    )
    normalized_explanation = _normalize_optional_text(explanation)

    existing = repository.get_alert_result_by_rule_key(
        db,
        source_domain=normalized_source_domain,
        source_record_id=validated_source_record_id,
        rule_code=normalized_rule_code,
    )
    if existing is None:
        return repository.create_alert_result(
            db,
            source_domain=normalized_source_domain,
            source_record_id=validated_source_record_id,
            rule_code=normalized_rule_code,
            severity=validated_severity,
            status=validated_status,
            title=normalized_title,
            message=normalized_message,
            explanation=normalized_explanation,
            triggered_at=triggered_at,
            viewed_at=viewed_at,
            dismissed_at=dismissed_at,
        )

    return repository.update_alert_result(
        db,
        alert_result=existing,
        severity=validated_severity,
        status=validated_status,
        title=normalized_title,
        message=normalized_message,
        explanation=normalized_explanation,
        triggered_at=triggered_at,
        viewed_at=viewed_at,
        dismissed_at=dismissed_at,
    )


def list_alert_reads(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int | None = None,
    status: str | None = None,
) -> AlertResultListRead:
    normalized_source_domain = _normalize_required_text(
        source_domain,
        field_name="source_domain",
        code="INVALID_ALERT_SOURCE_DOMAIN",
    )
    validated_source_record_id = (
        _validate_record_id(
            source_record_id,
            field_name="source_record_id",
            code="INVALID_ALERT_SOURCE_RECORD_ID",
        )
        if source_record_id is not None
        else None
    )
    validated_status = _validate_optional_alert_status(status)
    results = repository.list_alert_results(
        db,
        source_domain=normalized_source_domain,
        source_record_id=validated_source_record_id,
        status=validated_status,
    )
    return AlertResultListRead(items=[_build_alert_read(result) for result in results])


def mark_alert_as_viewed(
    db: Session,
    *,
    alert_id: int,
) -> AlertResultRead:
    try:
        alert_result = _get_alert_result_or_raise(db, alert_id)

        if alert_result.status == AlertStatus.DISMISSED:
            raise ConflictError(
                message=f"Alert result {alert_id} is already dismissed and cannot be marked as viewed.",
                code="ALERT_ALREADY_DISMISSED",
            )
        if alert_result.status == AlertStatus.OPEN:
            repository.update_alert_status(
                db,
                alert_result=alert_result,
                status=AlertStatus.VIEWED,
                viewed_at=_utcnow(),
                dismissed_at=alert_result.dismissed_at,
            )

        db.commit()
        return _build_alert_read(alert_result)
    except Exception:
        db.rollback()
        raise


def dismiss_alert(
    db: Session,
    *,
    alert_id: int,
) -> AlertResultRead:
    try:
        alert_result = _get_alert_result_or_raise(db, alert_id)

        if alert_result.status != AlertStatus.DISMISSED:
            repository.update_alert_status(
                db,
                alert_result=alert_result,
                status=AlertStatus.DISMISSED,
                viewed_at=alert_result.viewed_at,
                dismissed_at=_utcnow(),
            )

        db.commit()
        return _build_alert_read(alert_result)
    except Exception:
        db.rollback()
        raise


def _build_alert_read(result: AlertResult) -> AlertResultRead:
    return AlertResultRead(
        id=result.id,
        source_domain=result.source_domain,
        source_record_id=result.source_record_id,
        rule_code=result.rule_code,
        severity=result.severity,
        status=result.status,
        title=result.title,
        message=result.message,
        explanation=result.explanation,
        triggered_at=result.triggered_at,
        viewed_at=result.viewed_at,
        dismissed_at=result.dismissed_at,
        created_at=result.created_at,
    )


def _validate_alert_severity(value: str) -> str:
    normalized_value = _normalize_required_text(
        value,
        field_name="severity",
        code="INVALID_ALERT_SEVERITY",
    ).lower()
    if normalized_value not in _ALLOWED_ALERT_SEVERITIES:
        allowed = ", ".join(sorted(_ALLOWED_ALERT_SEVERITIES))
        raise BadRequestError(
            message=f"severity must be one of: {allowed}.",
            code="INVALID_ALERT_SEVERITY",
        )
    return normalized_value


def _validate_alert_status(value: str) -> str:
    normalized_value = _normalize_required_text(
        value,
        field_name="status",
        code="INVALID_ALERT_STATUS",
    ).lower()
    if normalized_value not in _ALLOWED_ALERT_STATUSES:
        allowed = ", ".join(sorted(_ALLOWED_ALERT_STATUSES))
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code="INVALID_ALERT_STATUS",
        )
    return normalized_value


def _validate_optional_alert_status(value: str | None) -> str | None:
    normalized_value = _normalize_optional_text(value)
    if normalized_value is None:
        return None
    return _validate_alert_status(normalized_value)


def _get_alert_result_or_raise(db: Session, alert_id: int) -> AlertResult:
    validated_alert_id = _validate_record_id(
        alert_id,
        field_name="alert_id",
        code="INVALID_ALERT_ID",
    )
    alert_result = repository.get_alert_result_by_id(db, validated_alert_id)
    if alert_result is None:
        raise NotFoundError(
            message=f"Alert result {validated_alert_id} was not found.",
            code="ALERT_NOT_FOUND",
        )
    return alert_result


def _validate_record_id(value: int, *, field_name: str, code: str) -> int:
    if value < 1:
        raise BadRequestError(
            message=f"{field_name} must be greater than or equal to 1.",
            code=code,
        )
    return value


def _normalize_required_text(value: str, *, field_name: str, code: str) -> str:
    normalized_value = _normalize_optional_text(value)
    if normalized_value is None:
        raise BadRequestError(
            message=f"{field_name} must not be empty.",
            code=code,
        )
    return normalized_value


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized_value = " ".join(str(value).split())
    return normalized_value or None


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
