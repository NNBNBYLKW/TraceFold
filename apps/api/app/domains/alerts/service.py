from __future__ import annotations

import logging
from datetime import UTC, datetime
from typing import Any

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import BadRequestError, IllegalStateError, NotFoundError
from app.core.logging import get_logger, log_event
from app.domains.alerts import repository
from app.domains.alerts.models import AlertSeverity, AlertStatus, RuleAlert
from app.domains.alerts.schemas import AlertListRead, AlertRead


logger = get_logger(__name__)
_ALLOWED_ALERT_SEVERITIES = {
    AlertSeverity.INFO.value,
    AlertSeverity.WARNING.value,
    AlertSeverity.HIGH.value,
}
_ALLOWED_ALERT_STATUSES = {
    AlertStatus.OPEN.value,
    AlertStatus.ACKNOWLEDGED.value,
    AlertStatus.RESOLVED.value,
    AlertStatus.INVALIDATED.value,
}


def upsert_rule_alert(
    db: Session,
    *,
    domain: str,
    rule_key: str,
    severity: str,
    source_record_type: str,
    source_record_id: int,
    message: str,
    details_json: dict[str, Any] | list[Any] | str | int | float | bool | None = None,
    triggered_at: datetime,
    status: str = AlertStatus.OPEN.value,
    acknowledged_at: datetime | None = None,
    resolved_at: datetime | None = None,
    resolution_note: str | None = None,
) -> RuleAlert:
    normalized_domain = _normalize_required_text(
        domain,
        field_name="domain",
        code=ErrorCode.INVALID_ALERT_DOMAIN,
    )
    normalized_rule_key = _normalize_required_text(
        rule_key,
        field_name="rule_key",
        code=ErrorCode.INVALID_ALERT_RULE_KEY,
    )
    normalized_source_record_type = _normalize_required_text(
        source_record_type,
        field_name="source_record_type",
        code=ErrorCode.BAD_REQUEST,
    )
    validated_source_record_id = _validate_record_id(
        source_record_id,
        field_name="source_record_id",
        code=ErrorCode.BAD_REQUEST,
    )
    validated_severity = _validate_alert_severity(severity)
    validated_status = _validate_alert_status(status)
    normalized_message = _normalize_required_text(
        message,
        field_name="message",
        code=ErrorCode.BAD_REQUEST,
    )
    normalized_resolution_note = _normalize_optional_text(resolution_note)
    normalized_details_json = _normalize_details_json(details_json)

    existing = repository.get_rule_alert_by_identity(
        db,
        domain=normalized_domain,
        source_record_type=normalized_source_record_type,
        source_record_id=validated_source_record_id,
        rule_key=normalized_rule_key,
    )
    if existing is None:
        alert = repository.create_rule_alert(
            db,
            domain=normalized_domain,
            rule_key=normalized_rule_key,
            severity=validated_severity,
            status=validated_status,
            source_record_type=normalized_source_record_type,
            source_record_id=validated_source_record_id,
            message=normalized_message,
            details_json=normalized_details_json,
            triggered_at=triggered_at,
            acknowledged_at=acknowledged_at,
            resolved_at=resolved_at,
            resolution_note=normalized_resolution_note,
        )
        log_event(
            logger,
            level=logging.INFO,
            event="alert_created",
            domain=normalized_domain,
            action="create",
            rule_key=alert.rule_key,
            alert_id=alert.id,
            source_record_type=alert.source_record_type,
            source_record_id=alert.source_record_id,
            result=alert.status,
        )
        return alert

    previous_status = existing.status
    alert = repository.update_rule_alert(
        db,
        alert=existing,
        severity=validated_severity,
        status=validated_status,
        message=normalized_message,
        details_json=normalized_details_json,
        triggered_at=triggered_at,
        acknowledged_at=acknowledged_at,
        resolved_at=resolved_at,
        resolution_note=normalized_resolution_note,
    )
    if previous_status != alert.status and alert.status == AlertStatus.OPEN.value:
        log_event(
            logger,
            level=logging.INFO,
            event="alert_reopened",
            domain=normalized_domain,
            action="reopen",
            rule_key=alert.rule_key,
            alert_id=alert.id,
            source_record_type=alert.source_record_type,
            source_record_id=alert.source_record_id,
            result=alert.status,
        )
    return alert


def upsert_alert_result(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    rule_code: str,
    severity: str,
    status: str = AlertStatus.OPEN.value,
    title: str,
    message: str,
    explanation: str | None = None,
    triggered_at: datetime,
    viewed_at: datetime | None = None,
    dismissed_at: datetime | None = None,
) -> RuleAlert:
    return upsert_rule_alert(
        db,
        domain=source_domain,
        rule_key=rule_code,
        severity=severity,
        source_record_type=_default_source_record_type_for_domain(source_domain),
        source_record_id=source_record_id,
        message=message,
        details_json=_build_legacy_details_json(title=title, explanation=explanation),
        triggered_at=triggered_at,
        status=_legacy_status_to_lifecycle_status(status),
        acknowledged_at=viewed_at,
        resolved_at=dismissed_at,
        resolution_note=None,
    )


def list_alert_reads(
    db: Session,
    *,
    domain: str | None = None,
    rule_key: str | None = None,
    status: str | None = None,
    limit: int = 50,
    source_record_type: str | None = None,
    source_record_id: int | None = None,
    source_domain: str | None = None,
) -> AlertListRead:
    normalized_domain = _coalesce_domain(domain=domain, source_domain=source_domain)
    normalized_rule_key = _normalize_optional_text(rule_key)
    validated_status = _validate_optional_alert_status(status)
    normalized_source_record_type = _normalize_optional_text(source_record_type)
    if normalized_source_record_type is None and normalized_domain is not None and source_record_id is not None:
        normalized_source_record_type = _default_source_record_type_for_domain(normalized_domain)
    validated_source_record_id = (
        _validate_record_id(
            source_record_id,
            field_name="source_record_id",
            code=ErrorCode.BAD_REQUEST,
        )
        if source_record_id is not None
        else None
    )
    normalized_limit = _validate_limit(limit)

    alerts = repository.list_alert_results(
        db,
        domain=normalized_domain,
        rule_key=normalized_rule_key,
        status=validated_status,
        source_record_type=normalized_source_record_type,
        source_record_id=validated_source_record_id,
        limit=normalized_limit,
    )
    total = repository.count_alert_results(
        db,
        domain=normalized_domain,
        status=validated_status,
        rule_key=normalized_rule_key,
    )
    return AlertListRead(
        items=[_build_alert_read(alert) for alert in alerts],
        limit=normalized_limit,
        total=total,
    )


def list_alert_reads_for_source(
    db: Session,
    *,
    domain: str,
    source_record_type: str,
    source_record_id: int,
) -> AlertListRead:
    alerts = repository.list_rule_alerts_by_source(
        db,
        domain=domain,
        source_record_type=source_record_type,
        source_record_id=source_record_id,
    )
    return AlertListRead(
        items=[_build_alert_read(alert) for alert in alerts],
        limit=len(alerts),
        total=len(alerts),
    )


def list_alert_models_for_source(
    db: Session,
    *,
    domain: str,
    source_record_type: str,
    source_record_id: int,
) -> list[RuleAlert]:
    return repository.list_rule_alerts_by_source(
        db,
        domain=domain,
        source_record_type=source_record_type,
        source_record_id=source_record_id,
    )


def get_alert_read(db: Session, alert_id: int) -> AlertRead:
    alert = _get_alert_or_raise(db, alert_id)
    return _build_alert_read(alert)


def acknowledge_alert(
    db: Session,
    *,
    alert_id: int,
) -> AlertRead:
    try:
        alert = _get_alert_or_raise(db, alert_id)
        if alert.status != AlertStatus.OPEN.value:
            raise IllegalStateError(
                message=f"Alert {alert_id} can only be acknowledged from open state.",
                code=ErrorCode.ALERT_STATE_CONFLICT,
                details={"alert_id": alert_id, "current_status": alert.status},
            )

        repository.update_alert_status(
            db,
            alert_result=alert,
            status=AlertStatus.ACKNOWLEDGED.value,
            viewed_at=_utcnow(),
            dismissed_at=alert.resolved_at,
            resolution_note=alert.resolution_note,
        )
        db.commit()
        db.refresh(alert)
        log_event(
            logger,
            level=logging.INFO,
            event="alert_acknowledged",
            domain=alert.domain,
            action="acknowledge",
            rule_key=alert.rule_key,
            alert_id=alert.id,
            source_record_type=alert.source_record_type,
            source_record_id=alert.source_record_id,
            result=alert.status,
        )
        return _build_alert_read(alert)
    except Exception:
        db.rollback()
        raise


def resolve_alert(
    db: Session,
    *,
    alert_id: int,
    resolution_note: str | None = None,
) -> AlertRead:
    try:
        alert = _get_alert_or_raise(db, alert_id)
        if alert.status not in {AlertStatus.OPEN.value, AlertStatus.ACKNOWLEDGED.value}:
            raise IllegalStateError(
                message=f"Alert {alert_id} cannot be resolved from {alert.status} state.",
                code=ErrorCode.ALERT_STATE_CONFLICT,
                details={"alert_id": alert_id, "current_status": alert.status},
            )

        repository.update_alert_status(
            db,
            alert_result=alert,
            status=AlertStatus.RESOLVED.value,
            viewed_at=alert.acknowledged_at,
            dismissed_at=_utcnow(),
            resolution_note=_normalize_optional_text(resolution_note),
        )
        db.commit()
        db.refresh(alert)
        log_event(
            logger,
            level=logging.INFO,
            event="alert_resolved",
            domain=alert.domain,
            action="resolve",
            rule_key=alert.rule_key,
            alert_id=alert.id,
            source_record_type=alert.source_record_type,
            source_record_id=alert.source_record_id,
            result=alert.status,
        )
        return _build_alert_read(alert)
    except Exception:
        db.rollback()
        raise


def invalidate_alert(
    db: Session,
    *,
    alert_id: int,
    resolution_note: str | None = None,
) -> RuleAlert:
    alert = _get_alert_or_raise(db, alert_id)
    if alert.status == AlertStatus.INVALIDATED.value:
        return alert

    repository.update_alert_status(
        db,
        alert_result=alert,
        status=AlertStatus.INVALIDATED.value,
        viewed_at=alert.acknowledged_at,
        dismissed_at=alert.resolved_at,
        resolution_note=_normalize_optional_text(resolution_note),
    )
    log_event(
        logger,
        level=logging.INFO,
        event="alert_invalidated",
        domain=alert.domain,
        action="invalidate",
        rule_key=alert.rule_key,
        alert_id=alert.id,
        source_record_type=alert.source_record_type,
        source_record_id=alert.source_record_id,
        result=alert.status,
    )
    return alert


def mark_alert_as_viewed(
    db: Session,
    *,
    alert_id: int,
) -> AlertRead:
    return acknowledge_alert(db, alert_id=alert_id)


def dismiss_alert(
    db: Session,
    *,
    alert_id: int,
) -> AlertRead:
    return resolve_alert(db, alert_id=alert_id)


def _build_alert_read(alert: RuleAlert) -> AlertRead:
    return AlertRead(
        id=alert.id,
        domain=alert.domain,
        rule_key=alert.rule_key,
        severity=alert.severity,
        status=alert.status,
        source_record_type=alert.source_record_type,
        source_record_id=alert.source_record_id,
        title=alert.title,
        message=alert.message,
        details_json=alert.details_json,
        source_domain=alert.domain,
        rule_code=alert.rule_key,
        explanation=alert.explanation,
        triggered_at=alert.triggered_at,
        acknowledged_at=alert.acknowledged_at,
        resolved_at=alert.resolved_at,
        viewed_at=alert.acknowledged_at,
        dismissed_at=alert.resolved_at,
        resolution_note=alert.resolution_note,
        created_at=alert.created_at,
        updated_at=alert.updated_at,
    )


def _validate_alert_severity(value: str) -> str:
    normalized_value = _normalize_required_text(
        value,
        field_name="severity",
        code=ErrorCode.INVALID_ALERT_SEVERITY,
    ).lower()
    if normalized_value not in _ALLOWED_ALERT_SEVERITIES:
        allowed = ", ".join(sorted(_ALLOWED_ALERT_SEVERITIES))
        raise BadRequestError(
            message=f"severity must be one of: {allowed}.",
            code=ErrorCode.INVALID_ALERT_SEVERITY,
        )
    return normalized_value


def _validate_alert_status(value: str) -> str:
    normalized_value = _legacy_status_to_lifecycle_status(
        _normalize_required_text(
            value,
            field_name="status",
            code=ErrorCode.INVALID_ALERT_STATUS,
        ).lower()
    )
    if normalized_value not in _ALLOWED_ALERT_STATUSES:
        allowed = ", ".join(sorted(_ALLOWED_ALERT_STATUSES))
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code=ErrorCode.INVALID_ALERT_STATUS,
        )
    return normalized_value


def _validate_optional_alert_status(value: str | None) -> str | None:
    normalized_value = _normalize_optional_text(value)
    if normalized_value is None:
        return None
    return _validate_alert_status(normalized_value)


def _get_alert_or_raise(db: Session, alert_id: int) -> RuleAlert:
    validated_alert_id = _validate_record_id(
        alert_id,
        field_name="alert_id",
        code=ErrorCode.BAD_REQUEST,
    )
    alert = repository.get_rule_alert_by_id(db, validated_alert_id)
    if alert is None:
        log_event(
            logger,
            level=logging.WARNING,
            event="alert_query_failed",
            domain="alerts",
            action="read",
            alert_id=validated_alert_id,
            result="not_found",
            error_code=ErrorCode.ALERT_NOT_FOUND,
        )
        raise NotFoundError(
            message=f"Alert {validated_alert_id} was not found.",
            code=ErrorCode.ALERT_NOT_FOUND,
        )
    return alert


def _validate_record_id(value: int, *, field_name: str, code: str) -> int:
    if value < 1:
        raise BadRequestError(
            message=f"{field_name} must be greater than or equal to 1.",
            code=code,
        )
    return value


def _validate_limit(limit: int) -> int:
    if limit < 1 or limit > 100:
        raise BadRequestError(
            message="limit must be between 1 and 100.",
            code=ErrorCode.BAD_REQUEST,
            details={"limit": limit},
        )
    return limit


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


def _normalize_details_json(
    value: dict[str, Any] | list[Any] | str | int | float | bool | None,
) -> dict[str, Any] | list[Any] | str | int | float | bool | None:
    if isinstance(value, dict):
        return {str(key): item for key, item in value.items()}
    return value


def _coalesce_domain(*, domain: str | None, source_domain: str | None) -> str | None:
    normalized_domain = _normalize_optional_text(domain)
    normalized_source_domain = _normalize_optional_text(source_domain)
    if normalized_domain is not None and normalized_source_domain is not None and normalized_domain != normalized_source_domain:
        raise BadRequestError(
            message="domain and source_domain must not conflict.",
            code=ErrorCode.INVALID_ALERT_DOMAIN,
        )
    return normalized_domain if normalized_domain is not None else normalized_source_domain


def _default_source_record_type_for_domain(domain: str) -> str:
    normalized_domain = " ".join(str(domain).split()).lower()
    if normalized_domain == "health":
        return "health_record"
    if normalized_domain == "expense":
        return "expense_record"
    if normalized_domain == "knowledge":
        return "knowledge_entry"
    return f"{normalized_domain}_record"


def _legacy_status_to_lifecycle_status(status: str) -> str:
    if status == "viewed":
        return AlertStatus.ACKNOWLEDGED.value
    if status == "dismissed":
        return AlertStatus.RESOLVED.value
    return status


def _build_legacy_details_json(*, title: str, explanation: str | None) -> dict[str, Any]:
    details_json: dict[str, Any] = {"title": " ".join(str(title).split())}
    if explanation is not None:
        normalized_explanation = _normalize_optional_text(explanation)
        if normalized_explanation is not None:
            details_json["explanation"] = normalized_explanation
    return details_json


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
