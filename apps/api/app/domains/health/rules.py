from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation
import re

from sqlalchemy.orm import Session

from app.domains.alerts import repository as alerts_repository
from app.domains.alerts.models import AlertResult, AlertSeverity, AlertStatus
from app.domains.alerts.service import upsert_alert_result
from app.domains.health.models import HealthRecord


RULE_SOURCE_DOMAIN = "health"

_INFO_EXPLANATION = (
    "This record is slightly outside the preferred range. "
    "It may be worth keeping an eye on if similar entries appear again."
)
_WARNING_EXPLANATION = (
    "This record is noticeably outside the usual range and may deserve attention. "
    "Consider reviewing the context of this entry."
)
_HIGH_EXPLANATION = (
    "This record is well outside the usual range and should not be ignored. "
    "Please consider checking the situation promptly."
)

_NUMERIC_VALUE_PATTERN = re.compile(r"^\d+(?:\.\d+)?$")
_BLOOD_PRESSURE_PATTERN = re.compile(r"^\d+/\d+$")

_HEART_RATE_RULE_CODES = {
    AlertSeverity.INFO: "HEALTH_HEART_RATE_INFO_V1",
    AlertSeverity.WARNING: "HEALTH_HEART_RATE_WARNING_V1",
    AlertSeverity.HIGH: "HEALTH_HEART_RATE_HIGH_V1",
}
_SLEEP_DURATION_RULE_CODES = {
    AlertSeverity.INFO: "HEALTH_SLEEP_DURATION_INFO_V1",
    AlertSeverity.WARNING: "HEALTH_SLEEP_DURATION_WARNING_V1",
    AlertSeverity.HIGH: "HEALTH_SLEEP_DURATION_HIGH_V1",
}
_BLOOD_PRESSURE_RULE_CODES = {
    AlertSeverity.INFO: "HEALTH_BLOOD_PRESSURE_INFO_V1",
    AlertSeverity.WARNING: "HEALTH_BLOOD_PRESSURE_WARNING_V1",
    AlertSeverity.HIGH: "HEALTH_BLOOD_PRESSURE_HIGH_V1",
}
_MANAGED_RULE_CODES = {
    *_HEART_RATE_RULE_CODES.values(),
    *_SLEEP_DURATION_RULE_CODES.values(),
    *_BLOOD_PRESSURE_RULE_CODES.values(),
}


@dataclass(frozen=True)
class HealthRuleMatch:
    rule_code: str
    severity: str
    title: str
    message: str
    explanation: str


def parse_heart_rate_value(value_text: str | None) -> Decimal | None:
    normalized_value = _normalize_value_text(value_text)
    if normalized_value is None or not _NUMERIC_VALUE_PATTERN.fullmatch(normalized_value):
        return None
    try:
        return Decimal(normalized_value)
    except InvalidOperation:
        return None


def parse_sleep_duration_value(value_text: str | None) -> Decimal | None:
    normalized_value = _normalize_value_text(value_text)
    if normalized_value is None or not _NUMERIC_VALUE_PATTERN.fullmatch(normalized_value):
        return None
    try:
        return Decimal(normalized_value)
    except InvalidOperation:
        return None


def parse_blood_pressure_value(value_text: str | None) -> tuple[int, int] | None:
    normalized_value = _normalize_value_text(value_text)
    if normalized_value is None or not _BLOOD_PRESSURE_PATTERN.fullmatch(normalized_value):
        return None
    systolic_text, diastolic_text = normalized_value.split("/", maxsplit=1)
    return int(systolic_text), int(diastolic_text)


def evaluate_health_rule_match(
    *,
    metric_type: str,
    value_text: str | None,
) -> HealthRuleMatch | None:
    normalized_metric_type = _normalize_metric_type(metric_type)

    if normalized_metric_type == "heart_rate":
        return _evaluate_heart_rate_rule(value_text)
    if normalized_metric_type == "sleep_duration":
        return _evaluate_sleep_duration_rule(value_text)
    if normalized_metric_type == "blood_pressure":
        return _evaluate_blood_pressure_rule(value_text)
    return None


def execute_health_rules_for_record(
    db: Session,
    *,
    health_record: HealthRecord,
) -> list[AlertResult]:
    current_results = alerts_repository.list_alert_results_by_source(
        db,
        source_domain=RULE_SOURCE_DOMAIN,
        source_record_id=health_record.id,
    )
    managed_results = [result for result in current_results if result.rule_code in _MANAGED_RULE_CODES]
    match = evaluate_health_rule_match(
        metric_type=health_record.metric_type,
        value_text=health_record.value_text,
    )

    if match is None:
        for result in managed_results:
            alerts_repository.delete_alert_result(db, alert_result=result)
        return []

    triggered_at = _utcnow()
    current_result = upsert_alert_result(
        db,
        source_domain=RULE_SOURCE_DOMAIN,
        source_record_id=health_record.id,
        rule_code=match.rule_code,
        severity=match.severity,
        status=AlertStatus.OPEN,
        title=match.title,
        message=match.message,
        explanation=match.explanation,
        triggered_at=triggered_at,
        viewed_at=None,
        dismissed_at=None,
    )
    for result in managed_results:
        if result.id != current_result.id:
            alerts_repository.delete_alert_result(db, alert_result=result)
    return [current_result]


def run_health_rules_on_record_create(
    db: Session,
    *,
    health_record: HealthRecord,
) -> list[AlertResult]:
    match = evaluate_health_rule_match(
        metric_type=health_record.metric_type,
        value_text=health_record.value_text,
    )
    if match is None:
        return []

    current_result = upsert_alert_result(
        db,
        source_domain=RULE_SOURCE_DOMAIN,
        source_record_id=health_record.id,
        rule_code=match.rule_code,
        severity=match.severity,
        status=AlertStatus.OPEN,
        title=match.title,
        message=match.message,
        explanation=match.explanation,
        triggered_at=_utcnow(),
        viewed_at=None,
        dismissed_at=None,
    )
    return [current_result]


def _evaluate_heart_rate_rule(value_text: str | None) -> HealthRuleMatch | None:
    value = parse_heart_rate_value(value_text)
    if value is None:
        return None

    if value >= Decimal("130"):
        severity = AlertSeverity.HIGH
    elif value >= Decimal("110"):
        severity = AlertSeverity.WARNING
    elif value >= Decimal("100"):
        severity = AlertSeverity.INFO
    else:
        return None

    display_value = _format_decimal(value)
    return HealthRuleMatch(
        rule_code=_HEART_RATE_RULE_CODES[severity],
        severity=severity,
        title="Elevated heart rate record",
        message=f"This heart rate entry of {display_value} bpm is above the usual range for this rule set.",
        explanation=_explanation_for_severity(severity),
    )


def _evaluate_sleep_duration_rule(value_text: str | None) -> HealthRuleMatch | None:
    value = parse_sleep_duration_value(value_text)
    if value is None:
        return None

    if value < Decimal("300"):
        severity = AlertSeverity.HIGH
    elif value < Decimal("360"):
        severity = AlertSeverity.WARNING
    elif value < Decimal("420"):
        severity = AlertSeverity.INFO
    else:
        return None

    display_value = _format_decimal(value)
    return HealthRuleMatch(
        rule_code=_SLEEP_DURATION_RULE_CODES[severity],
        severity=severity,
        title="Short sleep duration record",
        message=f"This sleep duration entry of {display_value} minutes is below the usual range for this rule set.",
        explanation=_explanation_for_severity(severity),
    )


def _evaluate_blood_pressure_rule(value_text: str | None) -> HealthRuleMatch | None:
    parsed_value = parse_blood_pressure_value(value_text)
    if parsed_value is None:
        return None

    systolic, diastolic = parsed_value
    if systolic >= 180 or diastolic >= 120:
        severity = AlertSeverity.HIGH
    elif 140 <= systolic <= 179 or 90 <= diastolic <= 119:
        severity = AlertSeverity.WARNING
    elif 130 <= systolic <= 139 or 80 <= diastolic <= 89:
        severity = AlertSeverity.INFO
    else:
        return None

    return HealthRuleMatch(
        rule_code=_BLOOD_PRESSURE_RULE_CODES[severity],
        severity=severity,
        title="Elevated blood pressure record",
        message=f"This blood pressure entry of {systolic}/{diastolic} is above the usual range for this rule set.",
        explanation=_explanation_for_severity(severity),
    )


def _normalize_metric_type(metric_type: str) -> str:
    return " ".join(str(metric_type).split()).lower()


def _normalize_value_text(value_text: str | None) -> str | None:
    if value_text is None:
        return None
    normalized_value = str(value_text).strip()
    return normalized_value or None


def _format_decimal(value: Decimal) -> str:
    if value == value.to_integral():
        return str(value.quantize(Decimal("1")))
    return format(value.normalize(), "f")


def _explanation_for_severity(severity: str) -> str:
    if severity == AlertSeverity.INFO:
        return _INFO_EXPLANATION
    if severity == AlertSeverity.WARNING:
        return _WARNING_EXPLANATION
    return _HIGH_EXPLANATION


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
