from __future__ import annotations

import logging

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError
from app.core.logging import build_log_message, get_logger, log_event
from app.domains.capture import repository
from app.domains.capture.models import CaptureRecord, ParseResult, ParseTargetDomain
from app.domains.capture.schemas import CaptureSubmitResultRead
from app.services.intake import service as intake_service


_DEFAULT_SOURCE_TYPE = "manual"
logger = get_logger(__name__)


def submit_capture(
    db: Session,
    *,
    source_type: str,
    source_ref: str | None = None,
    raw_text: str | None = None,
    raw_payload_json: dict | list | str | int | float | bool | None = None,
) -> CaptureRecord:
    return repository.create_capture(
        db,
        source_type=source_type,
        source_ref=source_ref,
        raw_text=raw_text,
        raw_payload_json=raw_payload_json,
    )


def save_parse_result(
    db: Session,
    *,
    capture_id: int,
    target_domain: str,
    confidence_score: float,
    confidence_level: str,
    parser_name: str,
    parser_version: str,
    parsed_payload_json: dict | list | str | int | float | bool | None = None,
) -> ParseResult:
    return repository.create_parse_result(
        db,
        capture_id=capture_id,
        target_domain=target_domain,
        confidence_score=confidence_score,
        confidence_level=confidence_level,
        parser_name=parser_name,
        parser_version=parser_version,
        parsed_payload_json=parsed_payload_json,
    )


def submit_capture_and_process(
    db: Session,
    *,
    raw_text: str,
    source_type: str = _DEFAULT_SOURCE_TYPE,
    source_ref: str | None = None,
) -> CaptureSubmitResultRead:
    validated_raw_text = _validate_raw_text(raw_text)
    validated_source_type = _validate_source_type(source_type)
    normalized_source_ref = _normalize_optional_text(source_ref)

    try:
        capture = intake_service.submit_capture(
            db,
            source_type=validated_source_type,
            source_ref=normalized_source_ref,
            raw_text=validated_raw_text,
        )
        outcome = intake_service.process_capture(db, capture=capture)
        db.commit()
        result = _build_capture_submit_result(capture=capture, outcome=outcome)
        log_event(
            logger,
            level=logging.INFO,
            event="capture_submit_completed",
            domain="capture",
            capture_id=capture.id,
            source_type=validated_source_type,
            route=result.route,
            target_domain=result.target_domain,
            pending_item_id=result.pending_item_id,
            formal_record_id=result.formal_record_id,
        )
        return result
    except Exception:
        db.rollback()
        logger.exception(
            build_log_message(
                "capture_submit_failed",
                domain="capture",
                source_type=validated_source_type,
            )
        )
        raise


def _build_capture_submit_result(
    *,
    capture: CaptureRecord,
    outcome: dict,
) -> CaptureSubmitResultRead:
    return CaptureSubmitResultRead(
        capture_created=True,
        capture_id=capture.id,
        status=capture.status,
        route=str(outcome["route"]),
        target_domain=str(outcome.get("target_domain", ParseTargetDomain.UNKNOWN)),
        pending_item_id=_as_optional_int(outcome.get("pending_item_id")),
        formal_record_id=_as_optional_int(outcome.get("record_id")),
    )


def _validate_raw_text(raw_text: str) -> str:
    if not raw_text.strip():
        raise BadRequestError(
            message="raw_text must not be empty.",
            code="INVALID_CAPTURE_RAW_TEXT",
        )
    return raw_text


def _validate_source_type(source_type: str) -> str:
    normalized_source_type = _normalize_optional_text(source_type)
    if normalized_source_type is None:
        raise BadRequestError(
            message="source_type must not be empty.",
            code="INVALID_CAPTURE_SOURCE_TYPE",
        )
    return normalized_source_type


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _as_optional_int(value: object) -> int | None:
    if value is None:
        return None
    return int(value)
