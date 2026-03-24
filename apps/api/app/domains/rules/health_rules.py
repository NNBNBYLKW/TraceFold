from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
import re

from app.domains.alerts.models import AlertSeverity


RULE_DOMAIN = "health"
RULE_SOURCE_RECORD_TYPE = "health_record"

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

_HEART_RATE_RULE_KEYS = {
    AlertSeverity.INFO.value: "HEALTH_HEART_RATE_INFO_V1",
    AlertSeverity.WARNING.value: "HEALTH_HEART_RATE_WARNING_V1",
    AlertSeverity.HIGH.value: "HEALTH_HEART_RATE_HIGH_V1",
}
_SLEEP_DURATION_RULE_KEYS = {
    AlertSeverity.INFO.value: "HEALTH_SLEEP_DURATION_INFO_V1",
    AlertSeverity.WARNING.value: "HEALTH_SLEEP_DURATION_WARNING_V1",
    AlertSeverity.HIGH.value: "HEALTH_SLEEP_DURATION_HIGH_V1",
}
_BLOOD_PRESSURE_RULE_KEYS = {
    AlertSeverity.INFO.value: "HEALTH_BLOOD_PRESSURE_INFO_V1",
    AlertSeverity.WARNING.value: "HEALTH_BLOOD_PRESSURE_WARNING_V1",
    AlertSeverity.HIGH.value: "HEALTH_BLOOD_PRESSURE_HIGH_V1",
}
MANAGED_RULE_KEYS = {
    *_HEART_RATE_RULE_KEYS.values(),
    *_SLEEP_DURATION_RULE_KEYS.values(),
    *_BLOOD_PRESSURE_RULE_KEYS.values(),
}


@dataclass(frozen=True)
class HealthRuleMatch:
    rule_key: str
    severity: str
    title: str
    message: str
    explanation: str

    @property
    def rule_code(self) -> str:
        return self.rule_key


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


def _evaluate_heart_rate_rule(value_text: str | None) -> HealthRuleMatch | None:
    value = parse_heart_rate_value(value_text)
    if value is None:
        return None

    if value >= Decimal("130"):
        severity = AlertSeverity.HIGH.value
    elif value >= Decimal("110"):
        severity = AlertSeverity.WARNING.value
    elif value >= Decimal("100"):
        severity = AlertSeverity.INFO.value
    else:
        return None

    display_value = _format_decimal(value)
    return HealthRuleMatch(
        rule_key=_HEART_RATE_RULE_KEYS[severity],
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
        severity = AlertSeverity.HIGH.value
    elif value < Decimal("360"):
        severity = AlertSeverity.WARNING.value
    elif value < Decimal("420"):
        severity = AlertSeverity.INFO.value
    else:
        return None

    display_value = _format_decimal(value)
    return HealthRuleMatch(
        rule_key=_SLEEP_DURATION_RULE_KEYS[severity],
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
        severity = AlertSeverity.HIGH.value
    elif 140 <= systolic <= 179 or 90 <= diastolic <= 119:
        severity = AlertSeverity.WARNING.value
    elif 130 <= systolic <= 139 or 80 <= diastolic <= 89:
        severity = AlertSeverity.INFO.value
    else:
        return None

    return HealthRuleMatch(
        rule_key=_BLOOD_PRESSURE_RULE_KEYS[severity],
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
    if severity == AlertSeverity.INFO.value:
        return _INFO_EXPLANATION
    if severity == AlertSeverity.WARNING.value:
        return _WARNING_EXPLANATION
    return _HIGH_EXPLANATION
