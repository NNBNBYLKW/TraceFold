from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.domains.ai_derivations.models import AiDerivation


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def create_ai_derivation(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str,
    status: str,
    model_key: str | None,
    model_version: str | None,
    source_basis_json: JsonValue = None,
    content_json: JsonValue = None,
    error_message: str | None = None,
    generated_at: datetime | None = None,
    invalidated_at: datetime | None = None,
) -> AiDerivation:
    ai_derivation = AiDerivation(
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
        status=status,
        model_key=model_key,
        model_version=model_version,
        source_basis_json=source_basis_json,
        content_json=content_json,
        error_message=error_message,
        generated_at=generated_at,
        invalidated_at=invalidated_at,
    )
    db.add(ai_derivation)
    db.flush()
    return ai_derivation


def get_ai_derivation_by_id(db: Session, derivation_id: int) -> AiDerivation | None:
    return db.get(AiDerivation, derivation_id)


def get_ai_derivation_by_identity(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str,
) -> AiDerivation | None:
    return (
        db.query(AiDerivation)
        .filter(AiDerivation.target_type == target_type)
        .filter(AiDerivation.target_id == target_id)
        .filter(AiDerivation.derivation_kind == derivation_kind)
        .first()
    )


def list_ai_derivations(
    db: Session,
    *,
    target_type: str | None = None,
    target_id: int | None = None,
    derivation_kind: str | None = None,
    status: str | None = None,
    limit: int = 50,
) -> list[AiDerivation]:
    query = db.query(AiDerivation)
    if target_type is not None:
        query = query.filter(AiDerivation.target_type == target_type)
    if target_id is not None:
        query = query.filter(AiDerivation.target_id == target_id)
    if derivation_kind is not None:
        query = query.filter(AiDerivation.derivation_kind == derivation_kind)
    if status is not None:
        query = query.filter(AiDerivation.status == status)
    return (
        query.order_by(AiDerivation.updated_at.desc(), AiDerivation.id.desc())
        .limit(limit)
        .all()
    )


def count_ai_derivations(
    db: Session,
    *,
    target_type: str | None = None,
    derivation_kind: str | None = None,
    status: str | None = None,
) -> int:
    query = db.query(AiDerivation)
    if target_type is not None:
        query = query.filter(AiDerivation.target_type == target_type)
    if derivation_kind is not None:
        query = query.filter(AiDerivation.derivation_kind == derivation_kind)
    if status is not None:
        query = query.filter(AiDerivation.status == status)
    return int(query.count())


def update_ai_derivation(
    db: Session,
    *,
    ai_derivation: AiDerivation,
    status: str,
    model_key: str | None,
    model_version: str | None,
    source_basis_json: JsonValue = None,
    content_json: JsonValue = None,
    error_message: str | None = None,
    generated_at: datetime | None = None,
    invalidated_at: datetime | None = None,
) -> AiDerivation:
    ai_derivation.status = status
    ai_derivation.model_key = model_key
    ai_derivation.model_version = model_version
    ai_derivation.source_basis_json = source_basis_json
    ai_derivation.content_json = content_json
    ai_derivation.error_message = error_message
    ai_derivation.generated_at = generated_at
    ai_derivation.invalidated_at = invalidated_at
    db.flush()
    return ai_derivation


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
) -> AiDerivation:
    return create_ai_derivation(
        db,
        target_type=target_domain,
        target_id=target_record_id,
        derivation_kind=derivation_type,
        status=status,
        model_key=model_name,
        model_version=model_version,
        source_basis_json=None,
        content_json=content_json,
        error_message=error_message,
        generated_at=generated_at,
        invalidated_at=failed_at if status == "invalidated" else None,
    )


def get_ai_derivation_result_by_type_key(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
    derivation_type: str,
) -> AiDerivation | None:
    return get_ai_derivation_by_identity(
        db,
        target_type=target_domain,
        target_id=target_record_id,
        derivation_kind=derivation_type,
    )


def list_ai_derivation_results_by_target(
    db: Session,
    *,
    target_domain: str,
    target_record_id: int,
) -> list[AiDerivation]:
    return list_ai_derivations(
        db,
        target_type=target_domain,
        target_id=target_record_id,
        limit=100,
    )


def update_ai_derivation_result(
    db: Session,
    *,
    ai_derivation_result: AiDerivation,
    status: str,
    model_name: str | None,
    model_version: str | None,
    generated_at: datetime | None,
    failed_at: datetime | None,
    content_json: JsonValue = None,
    error_message: str | None = None,
) -> AiDerivation:
    return update_ai_derivation(
        db,
        ai_derivation=ai_derivation_result,
        status=status,
        model_key=model_name,
        model_version=model_version,
        source_basis_json=ai_derivation_result.source_basis_json,
        content_json=content_json,
        error_message=error_message,
        generated_at=generated_at,
        invalidated_at=failed_at if status == "invalidated" else ai_derivation_result.invalidated_at,
    )
