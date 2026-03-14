from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.capture import repository
from app.domains.capture.models import CaptureRecord, ParseResult


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
