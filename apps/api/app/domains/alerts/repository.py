from __future__ import annotations

from datetime import datetime

from sqlalchemy import case
from sqlalchemy.orm import Session

from app.domains.alerts.models import AlertResult


_SEVERITY_ORDER = case(
    (AlertResult.severity == "high", 0),
    (AlertResult.severity == "warning", 1),
    (AlertResult.severity == "info", 2),
    else_=3,
)


def create_alert_result(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    rule_code: str,
    severity: str,
    status: str,
    title: str,
    message: str,
    explanation: str | None,
    triggered_at: datetime,
    viewed_at: datetime | None,
    dismissed_at: datetime | None,
) -> AlertResult:
    alert_result = AlertResult(
        source_domain=source_domain,
        source_record_id=source_record_id,
        rule_code=rule_code,
        severity=severity,
        status=status,
        title=title,
        message=message,
        explanation=explanation,
        triggered_at=triggered_at,
        viewed_at=viewed_at,
        dismissed_at=dismissed_at,
    )
    db.add(alert_result)
    db.flush()
    return alert_result


def get_alert_result_by_rule_key(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    rule_code: str,
) -> AlertResult | None:
    return (
        db.query(AlertResult)
        .filter(AlertResult.source_domain == source_domain)
        .filter(AlertResult.source_record_id == source_record_id)
        .filter(AlertResult.rule_code == rule_code)
        .first()
    )


def get_alert_result_by_id(db: Session, alert_id: int) -> AlertResult | None:
    return db.get(AlertResult, alert_id)


def list_alert_results(
    db: Session,
    *,
    source_domain: str | None = None,
    source_record_id: int | None = None,
    status: str | None = None,
    limit: int | None = None,
) -> list[AlertResult]:
    query = db.query(AlertResult)

    if source_domain is not None:
        query = query.filter(AlertResult.source_domain == source_domain)
    if source_record_id is not None:
        query = query.filter(AlertResult.source_record_id == source_record_id)
    if status is not None:
        query = query.filter(AlertResult.status == status)

    query = query.order_by(
        _SEVERITY_ORDER.asc(),
        AlertResult.triggered_at.desc(),
        AlertResult.id.desc(),
    )
    if limit is not None:
        query = query.limit(limit)
    return query.all()


def count_alert_results(
    db: Session,
    *,
    source_domain: str | None = None,
    status: str | None = None,
) -> int:
    query = db.query(AlertResult)
    if source_domain is not None:
        query = query.filter(AlertResult.source_domain == source_domain)
    if status is not None:
        query = query.filter(AlertResult.status == status)
    return query.count()


def list_alert_results_by_source(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
) -> list[AlertResult]:
    return list_alert_results(
        db,
        source_domain=source_domain,
        source_record_id=source_record_id,
    )


def update_alert_result(
    db: Session,
    *,
    alert_result: AlertResult,
    severity: str,
    status: str,
    title: str,
    message: str,
    explanation: str | None,
    triggered_at: datetime,
    viewed_at: datetime | None,
    dismissed_at: datetime | None,
) -> AlertResult:
    alert_result.severity = severity
    alert_result.status = status
    alert_result.title = title
    alert_result.message = message
    alert_result.explanation = explanation
    alert_result.triggered_at = triggered_at
    alert_result.viewed_at = viewed_at
    alert_result.dismissed_at = dismissed_at
    db.flush()
    return alert_result


def update_alert_status(
    db: Session,
    *,
    alert_result: AlertResult,
    status: str,
    viewed_at: datetime | None,
    dismissed_at: datetime | None,
) -> AlertResult:
    alert_result.status = status
    alert_result.viewed_at = viewed_at
    alert_result.dismissed_at = dismissed_at
    db.flush()
    return alert_result


def delete_alert_result(
    db: Session,
    *,
    alert_result: AlertResult,
) -> None:
    db.delete(alert_result)
    db.flush()
