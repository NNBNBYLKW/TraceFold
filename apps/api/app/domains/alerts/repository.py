from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import case
from sqlalchemy.orm import Session

from app.domains.alerts.models import RuleAlert


_SEVERITY_ORDER = case(
    (RuleAlert.severity == "high", 0),
    (RuleAlert.severity == "warning", 1),
    (RuleAlert.severity == "info", 2),
    else_=3,
)


def create_rule_alert(
    db: Session,
    *,
    domain: str,
    rule_key: str,
    severity: str,
    status: str,
    source_record_type: str,
    source_record_id: int,
    message: str,
    details_json: dict[str, Any] | list[Any] | str | int | float | bool | None,
    triggered_at: datetime,
    acknowledged_at: datetime | None,
    resolved_at: datetime | None,
    resolution_note: str | None,
) -> RuleAlert:
    alert = RuleAlert(
        domain=domain,
        rule_key=rule_key,
        severity=severity,
        status=status,
        source_record_type=source_record_type,
        source_record_id=source_record_id,
        message=message,
        details_json=details_json,
        triggered_at=triggered_at,
        acknowledged_at=acknowledged_at,
        resolved_at=resolved_at,
        resolution_note=resolution_note,
    )
    db.add(alert)
    db.flush()
    return alert


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
) -> RuleAlert:
    return create_rule_alert(
        db,
        domain=source_domain,
        rule_key=rule_code,
        severity=severity,
        status=status,
        source_record_type=_default_source_record_type_for_domain(source_domain),
        source_record_id=source_record_id,
        message=message,
        details_json=_build_legacy_details_json(title=title, explanation=explanation),
        triggered_at=triggered_at,
        acknowledged_at=viewed_at,
        resolved_at=dismissed_at,
        resolution_note=None,
    )


def get_rule_alert_by_identity(
    db: Session,
    *,
    domain: str,
    source_record_type: str,
    source_record_id: int,
    rule_key: str,
) -> RuleAlert | None:
    return (
        db.query(RuleAlert)
        .filter(RuleAlert.domain == domain)
        .filter(RuleAlert.source_record_type == source_record_type)
        .filter(RuleAlert.source_record_id == source_record_id)
        .filter(RuleAlert.rule_key == rule_key)
        .first()
    )


def get_alert_result_by_rule_key(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    rule_code: str,
) -> RuleAlert | None:
    return get_rule_alert_by_identity(
        db,
        domain=source_domain,
        source_record_type=_default_source_record_type_for_domain(source_domain),
        source_record_id=source_record_id,
        rule_key=rule_code,
    )


def get_rule_alert_by_id(db: Session, alert_id: int) -> RuleAlert | None:
    return db.get(RuleAlert, alert_id)


def get_alert_result_by_id(db: Session, alert_id: int) -> RuleAlert | None:
    return get_rule_alert_by_id(db, alert_id)


def list_rule_alerts(
    db: Session,
    *,
    domain: str | None = None,
    rule_key: str | None = None,
    source_record_type: str | None = None,
    source_record_id: int | None = None,
    status: str | None = None,
    limit: int | None = None,
) -> list[RuleAlert]:
    query = db.query(RuleAlert)

    if domain is not None:
        query = query.filter(RuleAlert.domain == domain)
    if rule_key is not None:
        query = query.filter(RuleAlert.rule_key == rule_key)
    if source_record_type is not None:
        query = query.filter(RuleAlert.source_record_type == source_record_type)
    if source_record_id is not None:
        query = query.filter(RuleAlert.source_record_id == source_record_id)
    if status is not None:
        query = query.filter(RuleAlert.status == status)

    query = query.order_by(
        _SEVERITY_ORDER.asc(),
        RuleAlert.triggered_at.desc(),
        RuleAlert.id.desc(),
    )
    if limit is not None:
        query = query.limit(limit)
    return query.all()


def list_alert_results(
    db: Session,
    *,
    source_domain: str | None = None,
    source_record_id: int | None = None,
    status: str | None = None,
    limit: int | None = None,
    rule_key: str | None = None,
    source_record_type: str | None = None,
    domain: str | None = None,
) -> list[RuleAlert]:
    normalized_domain = domain if domain is not None else source_domain
    normalized_source_record_type = source_record_type
    if normalized_source_record_type is None and normalized_domain is not None and source_record_id is not None:
        normalized_source_record_type = _default_source_record_type_for_domain(normalized_domain)

    return list_rule_alerts(
        db,
        domain=normalized_domain,
        rule_key=rule_key,
        source_record_type=normalized_source_record_type,
        source_record_id=source_record_id,
        status=status,
        limit=limit,
    )


def count_rule_alerts(
    db: Session,
    *,
    domain: str | None = None,
    status: str | None = None,
    rule_key: str | None = None,
) -> int:
    query = db.query(RuleAlert)
    if domain is not None:
        query = query.filter(RuleAlert.domain == domain)
    if status is not None:
        query = query.filter(RuleAlert.status == status)
    if rule_key is not None:
        query = query.filter(RuleAlert.rule_key == rule_key)
    return query.count()


def count_alert_results(
    db: Session,
    *,
    source_domain: str | None = None,
    status: str | None = None,
    rule_key: str | None = None,
    domain: str | None = None,
) -> int:
    return count_rule_alerts(
        db,
        domain=domain if domain is not None else source_domain,
        status=status,
        rule_key=rule_key,
    )


def list_rule_alerts_by_source(
    db: Session,
    *,
    domain: str,
    source_record_type: str,
    source_record_id: int,
) -> list[RuleAlert]:
    return list_rule_alerts(
        db,
        domain=domain,
        source_record_type=source_record_type,
        source_record_id=source_record_id,
    )


def list_alert_results_by_source(
    db: Session,
    *,
    source_domain: str,
    source_record_id: int,
    source_record_type: str | None = None,
) -> list[RuleAlert]:
    return list_alert_results(
        db,
        source_domain=source_domain,
        source_record_type=(
            source_record_type
            if source_record_type is not None
            else _default_source_record_type_for_domain(source_domain)
        ),
        source_record_id=source_record_id,
    )


def update_rule_alert(
    db: Session,
    *,
    alert: RuleAlert,
    severity: str,
    status: str,
    message: str,
    details_json: dict[str, Any] | list[Any] | str | int | float | bool | None,
    triggered_at: datetime,
    acknowledged_at: datetime | None,
    resolved_at: datetime | None,
    resolution_note: str | None,
) -> RuleAlert:
    alert.severity = severity
    alert.status = status
    alert.message = message
    alert.details_json = details_json
    alert.triggered_at = triggered_at
    alert.acknowledged_at = acknowledged_at
    alert.resolved_at = resolved_at
    alert.resolution_note = resolution_note
    db.flush()
    return alert


def update_alert_result(
    db: Session,
    *,
    alert_result: RuleAlert,
    severity: str,
    status: str,
    title: str,
    message: str,
    explanation: str | None,
    triggered_at: datetime,
    viewed_at: datetime | None,
    dismissed_at: datetime | None,
) -> RuleAlert:
    return update_rule_alert(
        db,
        alert=alert_result,
        severity=severity,
        status=status,
        message=message,
        details_json=_build_legacy_details_json(title=title, explanation=explanation),
        triggered_at=triggered_at,
        acknowledged_at=viewed_at,
        resolved_at=dismissed_at,
        resolution_note=None,
    )


def update_alert_status(
    db: Session,
    *,
    alert_result: RuleAlert,
    status: str,
    viewed_at: datetime | None,
    dismissed_at: datetime | None,
    resolution_note: str | None = None,
) -> RuleAlert:
    alert_result.status = status
    alert_result.acknowledged_at = viewed_at
    alert_result.resolved_at = dismissed_at
    alert_result.resolution_note = resolution_note
    db.flush()
    return alert_result


def delete_alert_result(
    db: Session,
    *,
    alert_result: RuleAlert,
) -> None:
    db.delete(alert_result)
    db.flush()


def _build_legacy_details_json(*, title: str, explanation: str | None) -> dict[str, Any]:
    details_json: dict[str, Any] = {"title": title}
    if explanation is not None:
        details_json["explanation"] = explanation
    return details_json


def _default_source_record_type_for_domain(domain: str) -> str:
    normalized_domain = " ".join(str(domain).split()).lower()
    if normalized_domain == "health":
        return "health_record"
    if normalized_domain == "expense":
        return "expense_record"
    if normalized_domain == "knowledge":
        return "knowledge_entry"
    return f"{normalized_domain}_record"
