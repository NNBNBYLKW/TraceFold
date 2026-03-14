from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseResult
from app.services.intake.parser import parse_raw_text


def submit_capture(
    db: Session,
    *,
    source_type: str,
    source_ref: str | None = None,
    raw_text: str | None = None,
    raw_payload_json: dict | list | str | int | float | bool | None = None,
) -> CaptureRecord:
    return capture_repository.create_capture(
        db,
        source_type=source_type,
        source_ref=source_ref,
        raw_text=raw_text,
        raw_payload_json=raw_payload_json,
        status=CaptureStatus.RECEIVED,
    )


def parse_capture(
    db: Session,
    *,
    capture: CaptureRecord,
    parser_name: str = "simple_keyword_parser",
    parser_version: str = "0.1.0",
) -> ParseResult:
    parsed = parse_raw_text(capture.raw_text)
    result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=parsed["target_domain"],
        confidence_score=parsed["confidence_score"],
        confidence_level=parsed["confidence_level"],
        parsed_payload_json=parsed["parsed_payload_json"],
        parser_name=parser_name,
        parser_version=parser_version,
    )
    capture.status = CaptureStatus.PARSED
    db.flush()
    return result
