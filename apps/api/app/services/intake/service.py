from __future__ import annotations

from datetime import datetime
import logging

from sqlalchemy.orm import Session

from app.core.logging import get_logger, log_event
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import (
    CaptureRecord,
    CaptureStatus,
    ParseConfidenceLevel,
    ParseResult,
    ParseTargetDomain,
)
from app.domains.expense.service import create_expense_record
from app.domains.health.service import create_health_record
from app.domains.knowledge.service import create_knowledge_entry
from app.domains.pending import repository as pending_repository
from app.services.intake.parser import parse_raw_text


logger = get_logger(__name__)


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
        parsed_payload_json=parsed.get("parsed_payload_json"),
        parser_name=parser_name,
        parser_version=parser_version,
    )

    capture.status = CaptureStatus.PARSED
    db.flush()
    log_event(
        logger,
        level=logging.INFO,
        event="capture_parsed",
        domain="capture",
        capture_id=capture.id,
        parse_result_id=result.id,
        target_domain=result.target_domain,
        confidence_level=result.confidence_level,
    )
    return result


def process_capture(
    db: Session,
    *,
    capture: CaptureRecord,
    parser_name: str = "simple_keyword_parser",
    parser_version: str = "0.1.0",
) -> dict:
    parse_result = parse_capture(
        db,
        capture=capture,
        parser_name=parser_name,
        parser_version=parser_version,
    )

    target_domain = parse_result.target_domain
    confidence_level = parse_result.confidence_level
    payload = parse_result.parsed_payload_json or {}

    should_go_pending = (
        target_domain == ParseTargetDomain.UNKNOWN
        or confidence_level in {ParseConfidenceLevel.LOW, ParseConfidenceLevel.MEDIUM}
    )

    if should_go_pending:
        pending_item = pending_repository.create_pending_item(
            db,
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            target_domain=target_domain,
            proposed_payload_json=payload,
            corrected_payload_json=None,
            reason="Needs manual confirmation.",
        )

        capture.status = CaptureStatus.PENDING
        db.flush()
        log_event(
            logger,
            level=logging.INFO,
            event="capture_routed_to_pending",
            domain="capture",
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            pending_item_id=pending_item.id,
            target_domain=target_domain,
            confidence_level=confidence_level,
        )

        return {
            "route": "pending",
            "capture_id": capture.id,
            "parse_result_id": parse_result.id,
            "pending_item_id": pending_item.id,
            "target_domain": target_domain,
            "confidence_level": confidence_level,
        }

    created_record_id: int | None = None

    if target_domain == ParseTargetDomain.EXPENSE:
        record = create_expense_record(
            db,
            source_capture_id=capture.id,
            source_pending_id=None,
            payload=payload,
        )
        created_record_id = record.id

    elif target_domain == ParseTargetDomain.KNOWLEDGE:
        record = create_knowledge_entry(
            db,
            source_capture_id=capture.id,
            source_pending_id=None,
            payload=payload,
        )
        created_record_id = record.id

    elif target_domain == ParseTargetDomain.HEALTH:
        record = create_health_record(
            db,
            source_capture_id=capture.id,
            source_pending_id=None,
            payload=payload,
        )
        created_record_id = record.id

    else:
        pending_item = pending_repository.create_pending_item(
            db,
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            target_domain=ParseTargetDomain.UNKNOWN,
            proposed_payload_json=payload,
            corrected_payload_json=None,
            reason="Unsupported parse target domain.",
        )

        capture.status = CaptureStatus.PENDING
        db.flush()
        log_event(
            logger,
            level=logging.INFO,
            event="capture_routed_to_pending",
            domain="capture",
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            pending_item_id=pending_item.id,
            target_domain=ParseTargetDomain.UNKNOWN,
            confidence_level=confidence_level,
            reason="unsupported_target_domain",
        )

        return {
            "route": "pending",
            "capture_id": capture.id,
            "parse_result_id": parse_result.id,
            "pending_item_id": pending_item.id,
            "target_domain": ParseTargetDomain.UNKNOWN,
            "confidence_level": confidence_level,
        }

    capture.status = CaptureStatus.COMMITTED
    capture.finalized_at = datetime.utcnow()
    db.flush()
    log_event(
        logger,
        level=logging.INFO,
        event="capture_committed_directly",
        domain="capture",
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=target_domain,
        record_id=created_record_id,
        confidence_level=confidence_level,
    )

    return {
        "route": "committed",
        "capture_id": capture.id,
        "parse_result_id": parse_result.id,
        "target_domain": target_domain,
        "record_id": created_record_id,
        "confidence_level": confidence_level,
    }
