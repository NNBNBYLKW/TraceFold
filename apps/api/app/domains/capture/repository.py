from __future__ import annotations

from typing import Any

from sqlalchemy.orm import Session

from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseResult


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def create_capture(
    db: Session,
    *,
    source_type: str,
    source_ref: str | None = None,
    raw_text: str | None = None,
    raw_payload_json: JsonValue = None,
    status: str = CaptureStatus.RECEIVED,
) -> CaptureRecord:
    capture = CaptureRecord(
        source_type=source_type,
        source_ref=source_ref,
        raw_text=raw_text,
        raw_payload_json=raw_payload_json,
        status=status,
    )
    db.add(capture)
    db.flush()
    return capture


def create_parse_result(
    db: Session,
    *,
    capture_id: int,
    target_domain: str,
    confidence_score: float,
    confidence_level: str,
    parser_name: str,
    parser_version: str,
    parsed_payload_json: JsonValue = None,
) -> ParseResult:
    result = ParseResult(
        capture_id=capture_id,
        target_domain=target_domain,
        confidence_score=confidence_score,
        confidence_level=confidence_level,
        parser_name=parser_name,
        parser_version=parser_version,
        parsed_payload_json=parsed_payload_json,
    )
    db.add(result)
    db.flush()
    return result


def get_capture_by_id(db: Session, capture_id: int) -> CaptureRecord | None:
    return db.get(CaptureRecord, capture_id)
