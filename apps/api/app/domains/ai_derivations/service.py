from __future__ import annotations

from datetime import UTC, datetime
import hashlib
from typing import Any

from sqlalchemy.orm import Session

from app.ai import service as ai_service
from app.core.error_codes import ErrorCode
from app.core.exceptions import BadRequestError, DerivationFailedError, IllegalStateError, NotFoundError
from app.core.logging import get_logger, log_event
from app.domains.ai_derivations import repository
from app.domains.ai_derivations.models import AiDerivation, AiDerivationResult, AiDerivationStatus
from app.domains.ai_derivations.schemas import (
    AiDerivationInvalidateRead,
    AiDerivationListRead,
    AiDerivationRead,
)


logger = get_logger(__name__)
JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None

FORMAL_TARGET_TYPE_KNOWLEDGE = "knowledge"
FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY = "knowledge_summary"

_ALLOWED_AI_DERIVATION_STATUSES = {
    AiDerivationStatus.PENDING,
    AiDerivationStatus.RUNNING,
    AiDerivationStatus.READY,
    AiDerivationStatus.FAILED,
    AiDerivationStatus.INVALIDATED,
    AiDerivationStatus.COMPLETED,
}


def upsert_ai_derivation(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str,
    status: str = AiDerivationStatus.PENDING,
    model_key: str | None = None,
    model_version: str | None = None,
    source_basis_json: JsonValue = None,
    content_json: JsonValue = None,
    error_message: str | None = None,
    generated_at: datetime | None = None,
    invalidated_at: datetime | None = None,
) -> AiDerivation:
    normalized_target_type = _normalize_required_text(
        target_type,
        field_name="target_type",
        code=ErrorCode.INVALID_DERIVATION_TARGET_TYPE,
    )
    validated_target_id = _validate_record_id(
        target_id,
        field_name="target_id",
        code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
    )
    normalized_derivation_kind = _normalize_required_text(
        derivation_kind,
        field_name="derivation_kind",
        code=ErrorCode.INVALID_DERIVATION_KIND,
    )
    validated_status = _validate_ai_derivation_status(status)
    normalized_model_key = _normalize_optional_text(model_key)
    normalized_model_version = _normalize_optional_text(model_version)
    normalized_error_message = _normalize_optional_text(error_message)

    existing = repository.get_ai_derivation_by_identity(
        db,
        target_type=normalized_target_type,
        target_id=validated_target_id,
        derivation_kind=normalized_derivation_kind,
    )
    if existing is None:
        return repository.create_ai_derivation(
            db,
            target_type=normalized_target_type,
            target_id=validated_target_id,
            derivation_kind=normalized_derivation_kind,
            status=validated_status,
            model_key=normalized_model_key,
            model_version=normalized_model_version,
            source_basis_json=source_basis_json,
            content_json=content_json,
            error_message=normalized_error_message,
            generated_at=generated_at,
            invalidated_at=invalidated_at,
        )

    return repository.update_ai_derivation(
        db,
        ai_derivation=existing,
        status=validated_status,
        model_key=normalized_model_key,
        model_version=normalized_model_version,
        source_basis_json=source_basis_json,
        content_json=content_json,
        error_message=normalized_error_message,
        generated_at=generated_at,
        invalidated_at=invalidated_at,
    )


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
    mapped_status = AiDerivationStatus.READY if status == AiDerivationStatus.COMPLETED else status
    return upsert_ai_derivation(
        db,
        target_type=target_domain,
        target_id=target_record_id,
        derivation_kind=derivation_type,
        status=mapped_status,
        model_key=model_name,
        model_version=model_version,
        source_basis_json=None,
        content_json=content_json,
        error_message=error_message,
        generated_at=generated_at,
        invalidated_at=failed_at if mapped_status == AiDerivationStatus.INVALIDATED else None,
    )


def list_ai_derivation_reads(
    db: Session,
    *,
    target_type: str | None = None,
    target_id: int | None = None,
    derivation_kind: str | None = None,
    status: str | None = None,
    limit: int = 50,
    target_domain: str | None = None,
    target_record_id: int | None = None,
) -> AiDerivationListRead:
    normalized_target_type = _normalize_optional_text(target_type) or _normalize_optional_text(target_domain)
    normalized_derivation_kind = _normalize_optional_text(derivation_kind)
    normalized_status = _normalize_optional_text(status)
    normalized_target_id = target_id if target_id is not None else target_record_id

    if normalized_status is not None:
        normalized_status = _validate_ai_derivation_status(normalized_status)
    if normalized_target_id is not None:
        normalized_target_id = _validate_record_id(
            normalized_target_id,
            field_name="target_id",
            code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
        )

    results = repository.list_ai_derivations(
        db,
        target_type=normalized_target_type,
        target_id=normalized_target_id,
        derivation_kind=normalized_derivation_kind,
        status=normalized_status,
        limit=_validate_limit(limit),
    )
    return AiDerivationListRead(items=[_build_ai_derivation_read(result) for result in results])


def get_ai_derivation_read(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str = FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
) -> AiDerivationRead:
    derivation = _get_ai_derivation_or_raise(
        db,
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
    )
    return _build_ai_derivation_read(derivation)


def invalidate_ai_derivation(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str = FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
) -> AiDerivationInvalidateRead:
    derivation = _get_ai_derivation_or_raise(
        db,
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
    )
    if derivation.status == AiDerivationStatus.RUNNING:
        raise IllegalStateError(
            message="Running derivation cannot be invalidated.",
            code=ErrorCode.DERIVATION_STATE_CONFLICT,
            details={
                "target_type": target_type,
                "target_id": target_id,
                "derivation_kind": derivation_kind,
                "current_status": derivation.status,
            },
        )

    invalidated_at = _utcnow()
    updated = upsert_ai_derivation(
        db,
        target_type=derivation.target_type,
        target_id=derivation.target_id,
        derivation_kind=derivation.derivation_kind,
        status=AiDerivationStatus.INVALIDATED,
        model_key=derivation.model_key,
        model_version=derivation.model_version,
        source_basis_json=derivation.source_basis_json,
        content_json=derivation.content_json,
        error_message=derivation.error_message,
        generated_at=derivation.generated_at,
        invalidated_at=invalidated_at,
    )
    log_event(
        logger,
        event="derivation_invalidated",
        domain="ai_derivations",
        action="invalidate",
        derivation_kind=updated.derivation_kind,
        derivation_id=updated.id,
        target_type=updated.target_type,
        target_id=updated.target_id,
        result=updated.status,
    )
    return AiDerivationInvalidateRead(
        id=updated.id,
        target_type=updated.target_type,
        target_id=updated.target_id,
        derivation_kind=updated.derivation_kind,
        status=updated.status,
        invalidated_at=invalidated_at,
    )


def request_knowledge_summary_recompute(
    db: Session,
    *,
    knowledge_id: int,
) -> dict[str, Any]:
    entry = _get_knowledge_entry_or_raise(db, knowledge_id=knowledge_id)
    basis = build_knowledge_source_basis(entry)
    model_metadata = _get_knowledge_summary_model_metadata()
    existing = repository.get_ai_derivation_by_identity(
        db,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=entry.id,
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
    )
    if existing is None:
        upsert_ai_derivation(
            db,
            target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
            target_id=entry.id,
            derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
            status=AiDerivationStatus.PENDING,
            model_key=model_metadata["model_key"],
            model_version=model_metadata["model_version"],
            source_basis_json=basis,
            content_json=None,
            error_message=None,
            generated_at=None,
            invalidated_at=None,
        )
    else:
        if existing.status == AiDerivationStatus.RUNNING:
            raise IllegalStateError(
                message="Knowledge summary derivation is already running.",
                code=ErrorCode.DERIVATION_STATE_CONFLICT,
                details={
                    "target_type": FORMAL_TARGET_TYPE_KNOWLEDGE,
                    "target_id": entry.id,
                    "derivation_kind": FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
                    "current_status": existing.status,
                },
            )
        upsert_ai_derivation(
            db,
            target_type=existing.target_type,
            target_id=existing.target_id,
            derivation_kind=existing.derivation_kind,
            status=AiDerivationStatus.INVALIDATED,
            model_key=model_metadata["model_key"],
            model_version=model_metadata["model_version"],
            source_basis_json=basis,
            content_json=existing.content_json,
            error_message=existing.error_message,
            generated_at=existing.generated_at,
            invalidated_at=_utcnow(),
        )

    return {
        "target_type": FORMAL_TARGET_TYPE_KNOWLEDGE,
        "target_id": entry.id,
        "derivation_kind": FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
    }


def log_knowledge_summary_recompute_requested(*, task_id: int, knowledge_id: int) -> None:
    log_event(
        logger,
        event="derivation_recompute_requested",
        domain="ai_derivations",
        action="recompute_request",
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=knowledge_id,
        task_id=task_id,
        result=AiDerivationStatus.PENDING,
    )


def execute_knowledge_summary_recompute_task(
    db: Session,
    payload_json: JsonValue,
    *,
    task_id: int | None = None,
) -> dict[str, Any]:
    payload = _validate_knowledge_summary_task_payload(payload_json)
    entry = _get_knowledge_entry_or_raise(db, knowledge_id=payload["target_id"])
    derivation = generate_knowledge_summary_now(
        db,
        knowledge_entry=entry,
        task_id=task_id,
        raise_on_failure=True,
    )
    return {
        "derivation_id": derivation.id,
        "target_type": derivation.target_type,
        "target_id": derivation.target_id,
        "derivation_kind": derivation.derivation_kind,
        "status": derivation.status,
        "model_key": derivation.model_key,
        "model_version": derivation.model_version,
    }


def generate_knowledge_summary_now(
    db: Session,
    *,
    knowledge_entry: Any,
    task_id: int | None = None,
    raise_on_failure: bool = False,
) -> AiDerivation:
    from app.domains.knowledge.ai_summary import build_knowledge_summary_content

    basis = build_knowledge_source_basis(knowledge_entry)
    model_metadata = _get_knowledge_summary_model_metadata()
    log_event(
        logger,
        event="derivation_requested",
        domain="ai_derivations",
        action="generate",
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=knowledge_entry.id,
        task_id=task_id,
        result=AiDerivationStatus.PENDING,
    )
    derivation = upsert_ai_derivation(
        db,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=knowledge_entry.id,
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        status=AiDerivationStatus.RUNNING,
        model_key=model_metadata["model_key"],
        model_version=model_metadata["model_version"],
        source_basis_json=basis,
        content_json=None,
        error_message=None,
        generated_at=None,
        invalidated_at=None,
    )
    log_event(
        logger,
        event="derivation_started",
        domain="ai_derivations",
        action="generate",
        derivation_kind=derivation.derivation_kind,
        derivation_id=derivation.id,
        target_type=derivation.target_type,
        target_id=derivation.target_id,
        task_id=task_id,
        result=derivation.status,
    )

    try:
        content = build_knowledge_summary_content(knowledge_entry)
    except Exception as exc:
        derivation = upsert_ai_derivation(
            db,
            target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
            target_id=knowledge_entry.id,
            derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
            status=AiDerivationStatus.FAILED,
            model_key=model_metadata["model_key"],
            model_version=model_metadata["model_version"],
            source_basis_json=basis,
            content_json=None,
            error_message=_build_error_message(exc),
            generated_at=None,
            invalidated_at=None,
        )
        log_event(
            logger,
            level=40,
            event="derivation_failed",
            domain="ai_derivations",
            action="generate",
            derivation_kind=derivation.derivation_kind,
            derivation_id=derivation.id,
            target_type=derivation.target_type,
            target_id=derivation.target_id,
            task_id=task_id,
            result=derivation.status,
            error_code=ErrorCode.DERIVATION_FAILED,
        )
        if raise_on_failure:
            raise DerivationFailedError(
                message="Knowledge summary derivation failed.",
                details={
                    "target_type": FORMAL_TARGET_TYPE_KNOWLEDGE,
                    "target_id": knowledge_entry.id,
                    "derivation_kind": FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
                },
            ) from exc
        return derivation

    derivation = upsert_ai_derivation(
        db,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=knowledge_entry.id,
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        status=AiDerivationStatus.READY,
        model_key=model_metadata["model_key"],
        model_version=model_metadata["model_version"],
        source_basis_json=basis,
        content_json=content,
        error_message=None,
        generated_at=_utcnow(),
        invalidated_at=None,
    )
    log_event(
        logger,
        event="derivation_ready",
        domain="ai_derivations",
        action="generate",
        derivation_kind=derivation.derivation_kind,
        derivation_id=derivation.id,
        target_type=derivation.target_type,
        target_id=derivation.target_id,
        task_id=task_id,
        result=derivation.status,
    )
    return derivation


def get_ai_derivation_status_summary(db: Session) -> dict[str, Any]:
    failed_count = repository.count_ai_derivations(
        db,
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        status=AiDerivationStatus.FAILED,
    )
    degraded_reasons: list[str] = []
    if failed_count > 0:
        degraded_reasons.append("ai_derivations_failed_present")
    return {
        "degraded_reasons": degraded_reasons,
        "failed_count": failed_count,
    }


def persist_derivation_failure_after_task_error(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str,
    error_message: str,
) -> None:
    normalized_target_type = _normalize_optional_text(target_type)
    normalized_derivation_kind = _normalize_optional_text(derivation_kind)
    if normalized_target_type != FORMAL_TARGET_TYPE_KNOWLEDGE:
        return
    if normalized_derivation_kind != FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY:
        return

    entry = _get_knowledge_entry_or_raise(db, knowledge_id=target_id)
    basis = build_knowledge_source_basis(entry)
    model_metadata = _get_knowledge_summary_model_metadata()
    upsert_ai_derivation(
        db,
        target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
        target_id=entry.id,
        derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        status=AiDerivationStatus.FAILED,
        model_key=model_metadata["model_key"],
        model_version=model_metadata["model_version"],
        source_basis_json=basis,
        content_json=None,
        error_message=_normalize_optional_text(error_message),
        generated_at=None,
        invalidated_at=None,
    )


def build_knowledge_source_basis(knowledge_entry: Any) -> dict[str, Any]:
    fingerprint_input = "||".join(
        [
            _normalize_optional_text(getattr(knowledge_entry, "title", None)) or "",
            _normalize_optional_text(getattr(knowledge_entry, "content", None)) or "",
            _normalize_optional_text(getattr(knowledge_entry, "source_text", None)) or "",
        ]
    )
    source_updated_at = getattr(knowledge_entry, "updated_at", None) or getattr(knowledge_entry, "created_at", None)
    return {
        "target_type": FORMAL_TARGET_TYPE_KNOWLEDGE,
        "target_id": knowledge_entry.id,
        "derivation_kind": FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        "source_capture_id": getattr(knowledge_entry, "source_capture_id", None),
        "source_pending_id": getattr(knowledge_entry, "source_pending_id", None),
        "content_fingerprint": hashlib.sha256(fingerprint_input.encode("utf-8")).hexdigest(),
        "source_updated_at": source_updated_at.isoformat() if isinstance(source_updated_at, datetime) else None,
    }


def _get_knowledge_summary_model_metadata() -> dict[str, str | None]:
    return ai_service.get_knowledge_summary_model_metadata()


def _build_ai_derivation_read(result: AiDerivation) -> AiDerivationRead:
    return AiDerivationRead.model_validate(result)


def _validate_ai_derivation_status(value: str) -> str:
    normalized_value = _normalize_required_text(
        value,
        field_name="status",
        code=ErrorCode.INVALID_DERIVATION_STATUS,
    ).lower()
    if normalized_value == AiDerivationStatus.COMPLETED:
        normalized_value = AiDerivationStatus.READY
    if normalized_value not in {
        AiDerivationStatus.PENDING,
        AiDerivationStatus.RUNNING,
        AiDerivationStatus.READY,
        AiDerivationStatus.FAILED,
        AiDerivationStatus.INVALIDATED,
    }:
        allowed = ", ".join(
            sorted(
                {
                    AiDerivationStatus.PENDING,
                    AiDerivationStatus.RUNNING,
                    AiDerivationStatus.READY,
                    AiDerivationStatus.FAILED,
                    AiDerivationStatus.INVALIDATED,
                }
            )
        )
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code=ErrorCode.INVALID_DERIVATION_STATUS,
        )
    return normalized_value


def _validate_knowledge_summary_task_payload(payload_json: JsonValue) -> dict[str, Any]:
    if not isinstance(payload_json, dict):
        raise BadRequestError(
            message="Task payload must be an object.",
            code=ErrorCode.BAD_REQUEST,
        )
    target_type = _normalize_required_text(
        str(payload_json.get("target_type", "")),
        field_name="target_type",
        code=ErrorCode.INVALID_DERIVATION_TARGET_TYPE,
    )
    derivation_kind = _normalize_required_text(
        str(payload_json.get("derivation_kind", "")),
        field_name="derivation_kind",
        code=ErrorCode.INVALID_DERIVATION_KIND,
    )
    target_id_value = payload_json.get("target_id")
    if not isinstance(target_id_value, int):
        raise BadRequestError(
            message="target_id must be an integer.",
            code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
        )
    target_id = _validate_record_id(
        target_id_value,
        field_name="target_id",
        code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
    )
    if target_type != FORMAL_TARGET_TYPE_KNOWLEDGE:
        raise BadRequestError(
            message="Only knowledge target_type is supported by the formal AI derivation baseline.",
            code=ErrorCode.INVALID_DERIVATION_TARGET_TYPE,
        )
    if derivation_kind != FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY:
        raise BadRequestError(
            message="Only knowledge_summary derivation_kind is supported by the formal AI derivation baseline.",
            code=ErrorCode.INVALID_DERIVATION_KIND,
        )
    return {
        "target_type": target_type,
        "target_id": target_id,
        "derivation_kind": derivation_kind,
    }


def _get_ai_derivation_or_raise(
    db: Session,
    *,
    target_type: str,
    target_id: int,
    derivation_kind: str,
) -> AiDerivation:
    derivation = repository.get_ai_derivation_by_identity(
        db,
        target_type=_normalize_required_text(
            target_type,
            field_name="target_type",
            code=ErrorCode.INVALID_DERIVATION_TARGET_TYPE,
        ),
        target_id=_validate_record_id(
            target_id,
            field_name="target_id",
            code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
        ),
        derivation_kind=_normalize_required_text(
            derivation_kind,
            field_name="derivation_kind",
            code=ErrorCode.INVALID_DERIVATION_KIND,
        ),
    )
    if derivation is None:
        log_event(
            logger,
            level=30,
            event="derivation_query_failed",
            domain="ai_derivations",
            action="read",
            derivation_kind=derivation_kind,
            target_type=target_type,
            target_id=target_id,
            result="not_found",
            error_code=ErrorCode.DERIVATION_NOT_FOUND,
        )
        raise NotFoundError(
            message="AI derivation was not found.",
            code=ErrorCode.DERIVATION_NOT_FOUND,
            details={
                "target_type": target_type,
                "target_id": target_id,
                "derivation_kind": derivation_kind,
            },
        )
    return derivation


def _get_knowledge_entry_or_raise(db: Session, *, knowledge_id: int) -> Any:
    from app.domains.knowledge import repository as knowledge_repository

    normalized_id = _validate_record_id(
        knowledge_id,
        field_name="target_id",
        code=ErrorCode.INVALID_DERIVATION_TARGET_ID,
    )
    entry = knowledge_repository.get_knowledge_entry_by_id(db, normalized_id)
    if entry is None:
        raise NotFoundError(
            message=f"Knowledge entry {normalized_id} was not found.",
            code="KNOWLEDGE_NOT_FOUND",
        )
    return entry


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


def _build_error_message(exc: Exception) -> str:
    message = _normalize_optional_text(str(exc))
    return message or "AI derivation failed."


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
