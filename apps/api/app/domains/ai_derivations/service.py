from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError
from app.domains.ai_derivations import repository
from app.domains.ai_derivations.models import AiDerivationResult, AiDerivationStatus
from app.domains.ai_derivations.schemas import AiDerivationResultListRead, AiDerivationResultRead


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None

_ALLOWED_AI_DERIVATION_STATUSES = {
    AiDerivationStatus.PENDING,
    AiDerivationStatus.COMPLETED,
    AiDerivationStatus.FAILED,
}


def upsert_ai_derivation_result(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
    derivation_type: str,
    status: str = AiDerivationStatus.PENDING,
    model_name: str | None = None,
    model_version: str | None = None,
    generated_at: datetime | None = None,
    failed_at: datetime | None = None,
    content_json: JsonValue = None,
    error_message: str | None = None,
) -> AiDerivationResult:
    normalized_target_domain = _normalize_required_text(
        target_domain,
        field_name="target_domain",
        code="INVALID_AI_DERIVATION_TARGET_DOMAIN",
    )
    validated_target_record_id = _validate_record_id(
        target_record_id,
        field_name="target_record_id",
        code="INVALID_AI_DERIVATION_TARGET_RECORD_ID",
    )
    normalized_derivation_type = _normalize_required_text(
        derivation_type,
        field_name="derivation_type",
        code="INVALID_AI_DERIVATION_TYPE",
    )
    validated_status = _validate_ai_derivation_status(status)
    normalized_model_name = _normalize_optional_text(model_name)
    normalized_model_version = _normalize_optional_text(model_version)
    normalized_error_message = _normalize_optional_text(error_message)

    existing = repository.get_ai_derivation_result_by_type_key(
        db,
        target_domain=normalized_target_domain,
        target_record_id=validated_target_record_id,
        derivation_type=normalized_derivation_type,
    )
    if existing is None:
        return repository.create_ai_derivation_result(
            db,
            target_domain=normalized_target_domain,
            target_record_id=validated_target_record_id,
            derivation_type=normalized_derivation_type,
            status=validated_status,
            model_name=normalized_model_name,
            model_version=normalized_model_version,
            generated_at=generated_at,
            failed_at=failed_at,
            content_json=content_json,
            error_message=normalized_error_message,
        )

    return repository.update_ai_derivation_result(
        db,
        ai_derivation_result=existing,
        status=validated_status,
        model_name=normalized_model_name,
        model_version=normalized_model_version,
        generated_at=generated_at,
        failed_at=failed_at,
        content_json=content_json,
        error_message=normalized_error_message,
    )


def list_ai_derivation_reads(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
) -> AiDerivationResultListRead:
    normalized_target_domain = _normalize_required_text(
        target_domain,
        field_name="target_domain",
        code="INVALID_AI_DERIVATION_TARGET_DOMAIN",
    )
    validated_target_record_id = _validate_record_id(
        target_record_id,
        field_name="target_record_id",
        code="INVALID_AI_DERIVATION_TARGET_RECORD_ID",
    )
    results = repository.list_ai_derivation_results_by_target(
        db,
        target_domain=normalized_target_domain,
        target_record_id=validated_target_record_id,
    )
    return AiDerivationResultListRead(items=[_build_ai_derivation_read(result) for result in results])


def _build_ai_derivation_read(result: AiDerivationResult) -> AiDerivationResultRead:
    return AiDerivationResultRead(
        id=result.id,
        target_domain=result.target_domain,
        target_record_id=result.target_record_id,
        derivation_type=result.derivation_type,
        status=result.status,
        model_name=result.model_name,
        model_version=result.model_version,
        generated_at=result.generated_at,
        failed_at=result.failed_at,
        content_json=result.content_json,
        error_message=result.error_message,
        created_at=result.created_at,
    )


def _validate_ai_derivation_status(value: str) -> str:
    normalized_value = _normalize_required_text(
        value,
        field_name="status",
        code="INVALID_AI_DERIVATION_STATUS",
    ).lower()
    if normalized_value not in _ALLOWED_AI_DERIVATION_STATUSES:
        allowed = ", ".join(sorted(_ALLOWED_AI_DERIVATION_STATUSES))
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code="INVALID_AI_DERIVATION_STATUS",
        )
    return normalized_value


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
