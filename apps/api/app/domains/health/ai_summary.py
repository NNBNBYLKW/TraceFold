from __future__ import annotations

from datetime import UTC, datetime
import re
from typing import Any

from sqlalchemy.orm import Session

from app.domains.ai_derivations.models import AiDerivationResult, AiDerivationStatus
from app.domains.ai_derivations.service import upsert_ai_derivation_result
from app.domains.health.models import HealthRecord


HEALTH_SUMMARY_DERIVATION_TYPE = "health_summary"
HEALTH_SUMMARY_MODEL_NAME = "tracefold-health-summary"
HEALTH_SUMMARY_MODEL_VERSION = "v1"

_RULE_METRIC_TYPES = {"heart_rate", "sleep_duration", "blood_pressure"}
_MEASUREMENT_VALUE_PATTERN = re.compile(r"^\d+(?:\.\d+)?(?:\s*[a-zA-Z%]+)?$")
_FRACTIONAL_MEASUREMENT_PATTERN = re.compile(r"^\d+/\d+$")
_HEALTH_SUMMARY_KEYS = {
    "summary",
    "observations",
    "suggested_follow_up",
    "care_level_note",
}
_SUGGESTED_FOLLOW_UP = (
    "Consider adding a little more context to this record, or noting whether similar patterns appear again."
)
_CARE_LEVEL_NOTE = (
    "This note is only a supportive interpretation and not a medical conclusion. "
    "If the same issue keeps appearing or becomes more concerning, consider seeking professional advice."
)


def run_health_summary_on_record_create(
    db: Session,
    *,
    health_record: HealthRecord,
) -> AiDerivationResult | None:
    if not is_subjective_health_record(health_record):
        return None
    return _generate_health_summary_result(db, health_record=health_record)


def rerun_health_summary_for_record(
    db: Session,
    *,
    health_record: HealthRecord,
) -> AiDerivationResult | None:
    if not is_subjective_health_record(health_record):
        return None
    return _generate_health_summary_result(db, health_record=health_record)


def is_subjective_health_record(health_record: HealthRecord) -> bool:
    normalized_metric_type = _normalize_metric_type(health_record.metric_type)
    if normalized_metric_type in _RULE_METRIC_TYPES:
        return False

    normalized_value_text = _normalize_optional_text(health_record.value_text)
    normalized_note = _normalize_optional_text(health_record.note)

    if _looks_like_subjective_text(normalized_value_text):
        return True
    if normalized_note is not None and (
        normalized_value_text is None or not _looks_like_measurement_value(normalized_value_text)
    ):
        return True
    return False


def build_health_summary_content(health_record: HealthRecord) -> dict[str, Any]:
    metric_label = _format_metric_label(health_record.metric_type)
    normalized_value_text = _normalize_optional_text(health_record.value_text)
    normalized_note = _normalize_optional_text(health_record.note)

    summary_parts = [f"This record captures a subjective health observation about {metric_label}."]
    if normalized_value_text is not None:
        summary_parts.append(f"The main description recorded here is {normalized_value_text}.")
    if normalized_note is not None:
        summary_parts.append("Additional personal context was included with the note.")

    observations = [f"Recorded topic: {metric_label}."]
    if normalized_value_text is not None:
        observations.append(f"Recorded description: {normalized_value_text}.")
    if normalized_note is not None:
        observations.append(f"Added context: {normalized_note}.")

    content = {
        "summary": " ".join(summary_parts),
        "observations": observations,
        "suggested_follow_up": _SUGGESTED_FOLLOW_UP,
        "care_level_note": _CARE_LEVEL_NOTE,
    }
    validate_health_summary_content(content)
    return content


def validate_health_summary_content(content: dict[str, Any]) -> None:
    if set(content.keys()) != _HEALTH_SUMMARY_KEYS:
        raise ValueError("health_summary content_json must contain exactly summary, observations, suggested_follow_up, and care_level_note.")

    if not isinstance(content["summary"], str) or not content["summary"].strip():
        raise ValueError("health_summary.summary must be a non-empty string.")
    if not isinstance(content["observations"], list) or any(
        not isinstance(item, str) or not item.strip() for item in content["observations"]
    ):
        raise ValueError("health_summary.observations must be a list of non-empty strings.")
    if not isinstance(content["suggested_follow_up"], str) or not content["suggested_follow_up"].strip():
        raise ValueError("health_summary.suggested_follow_up must be a non-empty string.")
    if not isinstance(content["care_level_note"], str) or not content["care_level_note"].strip():
        raise ValueError("health_summary.care_level_note must be a non-empty string.")


def _generate_health_summary_result(
    db: Session,
    *,
    health_record: HealthRecord,
) -> AiDerivationResult:
    upsert_ai_derivation_result(
        db,
        target_domain="health",
        target_record_id=health_record.id,
        derivation_type=HEALTH_SUMMARY_DERIVATION_TYPE,
        status=AiDerivationStatus.PENDING,
        model_name=HEALTH_SUMMARY_MODEL_NAME,
        model_version=HEALTH_SUMMARY_MODEL_VERSION,
        generated_at=None,
        failed_at=None,
        content_json=None,
        error_message=None,
    )

    try:
        content = build_health_summary_content(health_record)
    except Exception as exc:
        return upsert_ai_derivation_result(
            db,
            target_domain="health",
            target_record_id=health_record.id,
            derivation_type=HEALTH_SUMMARY_DERIVATION_TYPE,
            status=AiDerivationStatus.FAILED,
            model_name=HEALTH_SUMMARY_MODEL_NAME,
            model_version=HEALTH_SUMMARY_MODEL_VERSION,
            generated_at=None,
            failed_at=_utcnow(),
            content_json=None,
            error_message=_build_error_message(exc),
        )

    return upsert_ai_derivation_result(
        db,
        target_domain="health",
        target_record_id=health_record.id,
        derivation_type=HEALTH_SUMMARY_DERIVATION_TYPE,
        status=AiDerivationStatus.COMPLETED,
        model_name=HEALTH_SUMMARY_MODEL_NAME,
        model_version=HEALTH_SUMMARY_MODEL_VERSION,
        generated_at=_utcnow(),
        failed_at=None,
        content_json=content,
        error_message=None,
    )


def _normalize_metric_type(metric_type: str) -> str:
    return " ".join(str(metric_type).split()).lower()


def _format_metric_label(metric_type: str) -> str:
    return _normalize_metric_type(metric_type).replace("_", " ")


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized_value = " ".join(str(value).split())
    return normalized_value or None


def _looks_like_measurement_value(value_text: str) -> bool:
    return bool(
        _MEASUREMENT_VALUE_PATTERN.fullmatch(value_text)
        or _FRACTIONAL_MEASUREMENT_PATTERN.fullmatch(value_text)
    )


def _looks_like_subjective_text(value_text: str | None) -> bool:
    if value_text is None or _looks_like_measurement_value(value_text):
        return False
    return any(character.isalpha() for character in value_text) or any(
        "\u4e00" <= character <= "\u9fff" for character in value_text
    )


def _build_error_message(exc: Exception) -> str:
    message = _normalize_optional_text(str(exc))
    return message or "Health AI summary generation failed."


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
