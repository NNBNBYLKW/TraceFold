from __future__ import annotations

from datetime import datetime
import logging
from typing import Any

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError, NotFoundError
from app.core.logging import build_log_message, get_logger, log_event
from app.domains.capture import repository
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseResult, ParseTargetDomain
from app.domains.capture.schemas import (
    CaptureDetailRead,
    CaptureFormalResultRead,
    CaptureListItemRead,
    CaptureListRead,
    CapturePendingLinkRead,
    CaptureSubmitResultRead,
    ParseResultRead,
)
from app.domains.expense import repository as expense_repository
from app.domains.health import repository as health_repository
from app.domains.knowledge import repository as knowledge_repository
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingItem, PendingStatus
from app.services.intake import service as intake_service


_DEFAULT_SOURCE_TYPE = "manual"
_DEFAULT_SORT_BY = "created_at"
_ALLOWED_SORT_FIELDS = {"created_at", "status", "source_type"}
_ALLOWED_SORT_ORDERS = {"asc", "desc"}
_ALLOWED_CAPTURE_STATUSES = {
    CaptureStatus.RECEIVED,
    CaptureStatus.PARSED,
    CaptureStatus.PENDING,
    CaptureStatus.COMMITTED,
    CaptureStatus.DISCARDED,
    CaptureStatus.FAILED,
}
_FORMAL_TARGET_DOMAINS = {
    ParseTargetDomain.EXPENSE,
    ParseTargetDomain.KNOWLEDGE,
    ParseTargetDomain.HEALTH,
}
_PREVIEW_LENGTH = 120
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


def list_capture_reads(
    db: Session,
    *,
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    status: str | None = None,
    source_type: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
) -> CaptureListRead:
    validated_page, validated_page_size = _validate_pagination(page=page, page_size=page_size)
    validated_sort_by = _validate_sort_by(sort_by, default=_DEFAULT_SORT_BY)
    validated_sort_order = _validate_sort_order(sort_order)
    validated_status = _validate_status(status)
    validated_source_type = _normalize_optional_text(source_type)
    _validate_date_range(date_from=date_from, date_to=date_to)

    captures, total = repository.list_captures(
        db,
        page=validated_page,
        page_size=validated_page_size,
        sort_by=validated_sort_by,
        sort_order=validated_sort_order,
        status=validated_status,
        source_type=validated_source_type,
        date_from=date_from,
        date_to=date_to,
    )
    parse_results = repository.get_latest_parse_results_by_capture_ids(
        db,
        capture_ids=[capture.id for capture in captures],
    )
    pending_items = pending_repository.get_latest_pending_items_by_capture_ids(
        db,
        capture_ids=[capture.id for capture in captures],
    )

    return CaptureListRead(
        items=[
            _build_capture_list_item(
                capture,
                parse_result=parse_results.get(capture.id),
                pending_item=pending_items.get(capture.id),
            )
            for capture in captures
        ],
        page=validated_page,
        page_size=validated_page_size,
        total=total,
    )


def get_capture_read(db: Session, capture_id: int) -> CaptureDetailRead:
    capture = _get_capture_or_raise(db, capture_id)
    parse_result = repository.get_latest_parse_result_by_capture_id(db, capture.id)
    pending_item = pending_repository.get_latest_pending_item_by_capture_id(db, capture.id)
    formal_result = _build_formal_result_read(
        db,
        capture=capture,
        parse_result=parse_result,
        pending_item=pending_item,
    )

    current_stage = _resolve_current_stage(
        capture=capture,
        pending_item=pending_item,
        formal_result=formal_result,
    )

    return CaptureDetailRead(
        id=capture.id,
        status=capture.status,
        source_type=capture.source_type,
        source_ref=capture.source_ref,
        summary=_build_capture_summary(capture),
        target_domain=_resolve_target_domain(parse_result=parse_result, pending_item=pending_item, formal_result=formal_result),
        current_stage=current_stage,
        chain_summary=_build_chain_summary(
            capture=capture,
            parse_result=parse_result,
            pending_item=pending_item,
            formal_result=formal_result,
        ),
        raw_text=capture.raw_text,
        raw_payload_json=capture.raw_payload_json,
        created_at=capture.created_at,
        updated_at=_resolve_capture_updated_at(
            capture=capture,
            parse_result=parse_result,
            pending_item=pending_item,
            formal_result=formal_result,
        ),
        finalized_at=capture.finalized_at,
        parse_result=None if parse_result is None else ParseResultRead.model_validate(parse_result),
        pending_item=_build_pending_link_read(pending_item),
        formal_result=formal_result,
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


def _build_capture_list_item(
    capture: CaptureRecord,
    *,
    parse_result: ParseResult | None,
    pending_item: PendingItem | None,
) -> CaptureListItemRead:
    return CaptureListItemRead(
        id=capture.id,
        status=capture.status,
        source_type=capture.source_type,
        source_ref=capture.source_ref,
        summary=_build_capture_summary(capture),
        target_domain=_resolve_target_domain(parse_result=parse_result, pending_item=pending_item, formal_result=None),
        current_stage=_resolve_current_stage(capture=capture, pending_item=pending_item, formal_result=None),
        created_at=capture.created_at,
        updated_at=_resolve_capture_updated_at(
            capture=capture,
            parse_result=parse_result,
            pending_item=pending_item,
            formal_result=None,
        ),
    )


def _build_capture_summary(capture: CaptureRecord) -> str | None:
    raw_text = _normalize_optional_text(capture.raw_text)
    if raw_text is not None:
        return _build_preview(raw_text)

    raw_payload = capture.raw_payload_json
    if isinstance(raw_payload, dict):
        for key in ("note", "title", "content", "source_text", "raw_text"):
            value = _normalize_optional_text(_string_value(raw_payload.get(key)))
            if value is not None:
                return _build_preview(value)
    if raw_payload is not None:
        return _build_preview(_string_value(raw_payload))
    return None


def _build_chain_summary(
    *,
    capture: CaptureRecord,
    parse_result: ParseResult | None,
    pending_item: PendingItem | None,
    formal_result: CaptureFormalResultRead | None,
) -> str:
    if formal_result is not None:
        if formal_result.source_pending_id is not None:
            return (
                f"Capture #{capture.id} flowed into Pending #{formal_result.source_pending_id} and then resolved into "
                f"{formal_result.target_domain} record #{formal_result.record_id}."
            )
        return (
            f"Capture #{capture.id} was committed directly to {formal_result.target_domain} record "
            f"#{formal_result.record_id} without entering Pending."
        )

    if pending_item is not None:
        if pending_item.status == PendingStatus.OPEN:
            return f"Capture #{capture.id} is currently waiting in Pending #{pending_item.id} for formal review."
        return (
            f"Capture #{capture.id} reached Pending #{pending_item.id} and is resolved as "
            f"{pending_item.status}."
        )

    if parse_result is not None:
        return (
            f"Capture #{capture.id} has parse result #{parse_result.id} for target domain "
            f"{parse_result.target_domain}, but no pending or formal result linkage is available."
        )

    if capture.status == CaptureStatus.FAILED:
        return f"Capture #{capture.id} did not complete downstream processing."

    return f"Capture #{capture.id} is stored as an upstream input record and has not moved further in the main chain yet."


def _build_pending_link_read(pending_item: PendingItem | None) -> CapturePendingLinkRead | None:
    if pending_item is None:
        return None

    return CapturePendingLinkRead(
        id=pending_item.id,
        status=pending_item.status,
        target_domain=pending_item.target_domain,
        summary=_build_pending_summary(pending_item),
        actionable=pending_item.status == PendingStatus.OPEN,
        resolved_at=pending_item.resolved_at,
    )


def _build_formal_result_read(
    db: Session,
    *,
    capture: CaptureRecord,
    parse_result: ParseResult | None,
    pending_item: PendingItem | None,
) -> CaptureFormalResultRead | None:
    preferred_target_domain = _resolve_target_domain(parse_result=parse_result, pending_item=pending_item, formal_result=None)

    candidate_domains: list[str] = []
    if preferred_target_domain in _FORMAL_TARGET_DOMAINS:
        candidate_domains.append(str(preferred_target_domain))
    for domain in sorted(_FORMAL_TARGET_DOMAINS):
        if domain not in candidate_domains:
            candidate_domains.append(domain)

    for target_domain in candidate_domains:
        result = _lookup_formal_result_by_capture_id(db, capture_id=capture.id, target_domain=target_domain)
        if result is not None:
            return result
    return None


def _lookup_formal_result_by_capture_id(
    db: Session,
    *,
    capture_id: int,
    target_domain: str,
) -> CaptureFormalResultRead | None:
    if target_domain == ParseTargetDomain.EXPENSE:
        record = expense_repository.get_expense_record_by_source_capture_id(db, capture_id)
        if record is None:
            return None
        return CaptureFormalResultRead(
            target_domain=target_domain,
            record_id=record.id,
            summary=_build_formal_summary(
                target_domain=target_domain,
                payload={
                    "amount": record.amount,
                    "currency": record.currency,
                    "category": record.category,
                    "note": record.note,
                },
            ),
            source_pending_id=record.source_pending_id,
            created_at=record.created_at,
        )

    if target_domain == ParseTargetDomain.KNOWLEDGE:
        record = knowledge_repository.get_knowledge_entry_by_source_capture_id(db, capture_id)
        if record is None:
            return None
        return CaptureFormalResultRead(
            target_domain=target_domain,
            record_id=record.id,
            summary=_build_formal_summary(
                target_domain=target_domain,
                payload={
                    "title": record.title,
                    "content": record.content,
                    "source_text": record.source_text,
                },
            ),
            source_pending_id=record.source_pending_id,
            created_at=record.created_at,
        )

    if target_domain == ParseTargetDomain.HEALTH:
        record = health_repository.get_health_record_by_source_capture_id(db, capture_id)
        if record is None:
            return None
        return CaptureFormalResultRead(
            target_domain=target_domain,
            record_id=record.id,
            summary=_build_formal_summary(
                target_domain=target_domain,
                payload={
                    "metric_type": record.metric_type,
                    "value_text": record.value_text,
                    "note": record.note,
                },
            ),
            source_pending_id=record.source_pending_id,
            created_at=record.created_at,
        )

    return None


def _build_pending_summary(pending_item: PendingItem) -> str | None:
    payload = pending_item.corrected_payload_json if pending_item.corrected_payload_json is not None else pending_item.proposed_payload_json
    return _build_formal_summary(target_domain=pending_item.target_domain, payload=payload)


def _build_formal_summary(
    *,
    target_domain: str,
    payload: Any,
) -> str | None:
    payload_dict = _as_payload_dict(payload)

    if target_domain == ParseTargetDomain.EXPENSE and payload_dict is not None:
        amount = _string_value(payload_dict.get("amount"))
        currency = _string_value(payload_dict.get("currency"))
        category = _normalize_optional_text(_string_value(payload_dict.get("category")))
        note = _normalize_optional_text(_string_value(payload_dict.get("note")))
        headline = " ".join(part for part in (amount, currency) if part).strip() or "Expense capture"
        detail = category or note
        return _build_preview(" · ".join(part for part in (headline, detail) if part))

    if target_domain == ParseTargetDomain.KNOWLEDGE and payload_dict is not None:
        return _build_preview(
            _string_value(payload_dict.get("title"))
            or _string_value(payload_dict.get("content"))
            or _string_value(payload_dict.get("source_text"))
            or "Knowledge capture"
        )

    if target_domain == ParseTargetDomain.HEALTH and payload_dict is not None:
        metric_type = _string_value(payload_dict.get("metric_type"))
        value_text = _string_value(payload_dict.get("value_text"))
        note = _normalize_optional_text(_string_value(payload_dict.get("note")))
        return _build_preview(" · ".join(part for part in (metric_type, value_text, note) if part))

    return None


def _resolve_target_domain(
    *,
    parse_result: ParseResult | None,
    pending_item: PendingItem | None,
    formal_result: CaptureFormalResultRead | None,
) -> str | None:
    if formal_result is not None:
        return formal_result.target_domain
    if pending_item is not None:
        return pending_item.target_domain
    if parse_result is not None:
        return parse_result.target_domain
    return None


def _resolve_current_stage(
    *,
    capture: CaptureRecord,
    pending_item: PendingItem | None,
    formal_result: CaptureFormalResultRead | None,
) -> str:
    if formal_result is not None:
        return "formal_record"
    if pending_item is not None and pending_item.status == PendingStatus.OPEN:
        return "pending_review"
    if pending_item is not None and pending_item.status == PendingStatus.DISCARDED:
        return "discarded"
    if capture.status == CaptureStatus.PARSED:
        return "parsed"
    if capture.status == CaptureStatus.RECEIVED:
        return "received"
    if capture.status == CaptureStatus.FAILED:
        return "failed"
    if capture.status == CaptureStatus.COMMITTED:
        return "formal_record"
    return capture.status


def _resolve_capture_updated_at(
    *,
    capture: CaptureRecord,
    parse_result: ParseResult | None,
    pending_item: PendingItem | None,
    formal_result: CaptureFormalResultRead | None,
) -> datetime:
    timestamps = [capture.created_at]
    if capture.finalized_at is not None:
        timestamps.append(capture.finalized_at)
    if parse_result is not None:
        timestamps.append(parse_result.created_at)
    if pending_item is not None:
        timestamps.append(pending_item.created_at)
        if pending_item.resolved_at is not None:
            timestamps.append(pending_item.resolved_at)
    if formal_result is not None:
        timestamps.append(formal_result.created_at)
    return max(timestamps)


def _get_capture_or_raise(db: Session, capture_id: int) -> CaptureRecord:
    capture = repository.get_capture_by_id(db, capture_id)
    if capture is None:
        raise NotFoundError(
            message=f"Capture {capture_id} was not found.",
            code="CAPTURE_NOT_FOUND",
        )
    return capture


def _validate_pagination(*, page: int, page_size: int) -> tuple[int, int]:
    if page < 1:
        raise BadRequestError(
            message="page must be greater than or equal to 1.",
            code="INVALID_PAGE",
        )
    if page_size < 1 or page_size > 100:
        raise BadRequestError(
            message="page_size must be between 1 and 100.",
            code="INVALID_PAGE_SIZE",
        )
    return page, page_size


def _validate_sort_by(sort_by: str | None, *, default: str) -> str:
    if sort_by is None:
        return default
    normalized_sort_by = sort_by.strip()
    if normalized_sort_by not in _ALLOWED_SORT_FIELDS:
        allowed = ", ".join(sorted(_ALLOWED_SORT_FIELDS))
        raise BadRequestError(
            message=f"sort_by must be one of: {allowed}.",
            code="INVALID_SORT_BY",
        )
    return normalized_sort_by


def _validate_sort_order(sort_order: str) -> str:
    normalized_sort_order = sort_order.strip().lower()
    if normalized_sort_order not in _ALLOWED_SORT_ORDERS:
        raise BadRequestError(
            message="sort_order must be either asc or desc.",
            code="INVALID_SORT_ORDER",
        )
    return normalized_sort_order


def _validate_status(status: str | None) -> str | None:
    normalized_status = _normalize_optional_text(status)
    if normalized_status is None:
        return None
    if normalized_status not in _ALLOWED_CAPTURE_STATUSES:
        allowed = ", ".join(sorted(_ALLOWED_CAPTURE_STATUSES))
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code="INVALID_STATUS",
        )
    return normalized_status


def _validate_date_range(*, date_from: datetime | None, date_to: datetime | None) -> None:
    if date_from is not None and date_to is not None and date_from > date_to:
        raise BadRequestError(
            message="date_from must be earlier than or equal to date_to.",
            code="INVALID_DATE_RANGE",
        )


def _build_preview(value: str | None) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    if len(normalized) <= _PREVIEW_LENGTH:
        return normalized
    return normalized[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _as_payload_dict(payload: Any) -> dict[str, Any] | None:
    if not isinstance(payload, dict):
        return None
    return payload


def _string_value(value: Any) -> str:
    if value is None:
        return ""
    return str(value).strip()


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
