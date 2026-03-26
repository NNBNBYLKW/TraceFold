from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import func
from sqlalchemy.orm import Session

from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseResult


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None
_SORT_COLUMNS = {
    "created_at": CaptureRecord.created_at,
    "status": func.lower(CaptureRecord.status),
    "source_type": func.lower(CaptureRecord.source_type),
}


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


def list_captures(
    db: Session,
    *,
    page: int,
    page_size: int,
    sort_by: str,
    sort_order: str,
    status: str | None = None,
    source_type: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
) -> tuple[list[CaptureRecord], int]:
    query = db.query(CaptureRecord)

    if status is not None:
        query = query.filter(CaptureRecord.status == status)
    if source_type is not None:
        query = query.filter(CaptureRecord.source_type == source_type)
    if date_from is not None:
        query = query.filter(CaptureRecord.created_at >= date_from)
    if date_to is not None:
        query = query.filter(CaptureRecord.created_at <= date_to)

    total = query.count()
    order_column = _SORT_COLUMNS[sort_by]
    order_clause = order_column.asc() if sort_order == "asc" else order_column.desc()
    items = (
        query.order_by(order_clause, CaptureRecord.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
        .all()
    )
    return items, total


def get_latest_parse_result_by_capture_id(db: Session, capture_id: int) -> ParseResult | None:
    return (
        db.query(ParseResult)
        .filter(ParseResult.capture_id == capture_id)
        .order_by(ParseResult.created_at.desc(), ParseResult.id.desc())
        .first()
    )


def get_latest_parse_results_by_capture_ids(
    db: Session,
    *,
    capture_ids: list[int],
) -> dict[int, ParseResult]:
    if not capture_ids:
        return {}

    rows = (
        db.query(ParseResult)
        .filter(ParseResult.capture_id.in_(capture_ids))
        .order_by(ParseResult.capture_id.asc(), ParseResult.created_at.desc(), ParseResult.id.desc())
        .all()
    )

    parse_results: dict[int, ParseResult] = {}
    for row in rows:
        parse_results.setdefault(int(row.capture_id), row)
    return parse_results


def list_captures_for_export(db: Session) -> list[CaptureRecord]:
    return (
        db.query(CaptureRecord)
        .order_by(CaptureRecord.created_at.asc(), CaptureRecord.id.asc())
        .all()
    )
