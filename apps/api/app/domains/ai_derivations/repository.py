from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.domains.ai_derivations.models import AiDerivationResult


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def create_ai_derivation_result(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
    derivation_type: str,
    status: str,
    model_name: str | None,
    model_version: str | None,
    generated_at: datetime | None,
    failed_at: datetime | None,
    content_json: JsonValue = None,
    error_message: str | None = None,
) -> AiDerivationResult:
    ai_derivation_result = AiDerivationResult(
        target_domain=target_domain,
        target_record_id=target_record_id,
        derivation_type=derivation_type,
        status=status,
        model_name=model_name,
        model_version=model_version,
        generated_at=generated_at,
        failed_at=failed_at,
        content_json=content_json,
        error_message=error_message,
    )
    db.add(ai_derivation_result)
    db.flush()
    return ai_derivation_result


def get_ai_derivation_result_by_type_key(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
    derivation_type: str,
) -> AiDerivationResult | None:
    return (
        db.query(AiDerivationResult)
        .filter(AiDerivationResult.target_domain == target_domain)
        .filter(AiDerivationResult.target_record_id == target_record_id)
        .filter(AiDerivationResult.derivation_type == derivation_type)
        .first()
    )


def list_ai_derivation_results_by_target(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
) -> list[AiDerivationResult]:
    return (
        db.query(AiDerivationResult)
        .filter(AiDerivationResult.target_domain == target_domain)
        .filter(AiDerivationResult.target_record_id == target_record_id)
        .order_by(AiDerivationResult.created_at.desc(), AiDerivationResult.id.desc())
        .all()
    )


def update_ai_derivation_result(
    db: Session,
    *,
    ai_derivation_result: AiDerivationResult,
    status: str,
    model_name: str | None,
    model_version: str | None,
    generated_at: datetime | None,
    failed_at: datetime | None,
    content_json: JsonValue = None,
    error_message: str | None = None,
) -> AiDerivationResult:
    ai_derivation_result.status = status
    ai_derivation_result.model_name = model_name
    ai_derivation_result.model_version = model_version
    ai_derivation_result.generated_at = generated_at
    ai_derivation_result.failed_at = failed_at
    ai_derivation_result.content_json = content_json
    ai_derivation_result.error_message = error_message
    db.flush()
    return ai_derivation_result
