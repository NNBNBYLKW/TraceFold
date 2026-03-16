from __future__ import annotations

from datetime import datetime
from typing import Any, cast

from sqlalchemy.orm import Session

from app.core.exceptions import BadRequestError, ConflictError, NotFoundError
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseTargetDomain
from app.domains.expense.service import create_expense_record
from app.domains.health.service import create_health_record
from app.domains.knowledge.service import create_knowledge_entry
from app.domains.pending import repository
from app.domains.pending.models import PendingActionType, PendingItem, PendingReviewAction, PendingStatus
from app.domains.pending.schemas import PendingDetailRead, PendingListItemRead, PendingListRead


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None
ResolvedPendingStatuses = {
    PendingStatus.CONFIRMED,
    PendingStatus.DISCARDED,
    PendingStatus.FORCED,
}
_DEFAULT_SORT_BY = "created_at"
_ALLOWED_SORT_FIELDS = {"created_at", "resolved_at", "status", "target_domain"}
_ALLOWED_SORT_ORDERS = {"asc", "desc"}
_ALLOWED_STATUSES = {
    PendingStatus.OPEN,
    PendingStatus.CONFIRMED,
    PendingStatus.DISCARDED,
    PendingStatus.FORCED,
}
_ALLOWED_TARGET_DOMAINS = {
    ParseTargetDomain.EXPENSE,
    ParseTargetDomain.KNOWLEDGE,
    ParseTargetDomain.HEALTH,
    ParseTargetDomain.UNKNOWN,
}
_PREVIEW_LENGTH = 120


def create_pending(
    db: Session,
    *,
    capture_id: int,
    parse_result_id: int,
    target_domain: str,
    proposed_payload_json: dict | list | str | int | float | bool | None = None,
    reason: str | None = None,
) -> PendingItem:
    return repository.create_pending_item(
        db,
        capture_id=capture_id,
        parse_result_id=parse_result_id,
        target_domain=target_domain,
        proposed_payload_json=proposed_payload_json,
        reason=reason,
    )


def list_pending_reads(
    db: Session,
    *,
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    status: str | None = None,
    target_domain: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
) -> PendingListRead:
    validated_page, validated_page_size = _validate_pagination(page=page, page_size=page_size)
    validated_sort_by = _validate_sort_by(sort_by, default=_DEFAULT_SORT_BY)
    validated_sort_order = _validate_sort_order(sort_order)
    validated_status = _validate_status(status)
    validated_target_domain = _validate_target_domain(target_domain)
    _validate_date_range(date_from=date_from, date_to=date_to)

    items, total = repository.list_pending_items(
        db,
        page=validated_page,
        page_size=validated_page_size,
        sort_by=validated_sort_by,
        sort_order=validated_sort_order,
        status=validated_status,
        target_domain=validated_target_domain,
        date_from=date_from,
        date_to=date_to,
    )
    next_pending_item = repository.get_oldest_open_pending_item(db)
    next_pending_item_id = next_pending_item.id if next_pending_item is not None else None

    return PendingListRead(
        items=[
            _build_pending_list_item(
                pending_item,
                next_pending_item_id=next_pending_item_id,
            )
            for pending_item in items
        ],
        page=validated_page,
        page_size=validated_page_size,
        total=total,
        next_pending_item_id=next_pending_item_id,
    )


def get_pending_read(db: Session, pending_item_id: int) -> PendingDetailRead:
    pending_item = _get_pending_item_or_raise(db, pending_item_id)
    return PendingDetailRead(
        id=pending_item.id,
        status=pending_item.status,
        target_domain=pending_item.target_domain,
        reason=pending_item.reason,
        proposed_payload_json=pending_item.proposed_payload_json,
        corrected_payload_json=pending_item.corrected_payload_json,
        created_at=pending_item.created_at,
        resolved_at=pending_item.resolved_at,
        source_capture_id=pending_item.capture_id,
        parse_result_id=pending_item.parse_result_id,
    )


def _build_pending_list_item(
    pending_item: PendingItem,
    *,
    next_pending_item_id: int | None,
) -> PendingListItemRead:
    return PendingListItemRead(
        id=pending_item.id,
        status=pending_item.status,
        target_domain=pending_item.target_domain,
        reason_preview=_build_preview(pending_item.reason),
        created_at=pending_item.created_at,
        has_corrected_payload=pending_item.corrected_payload_json is not None,
        source_capture_id=pending_item.capture_id,
        is_next_to_review=pending_item.id == next_pending_item_id,
    )


def _build_preview(value: str | None) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    if len(normalized) <= _PREVIEW_LENGTH:
        return normalized
    return normalized[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


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


def _validate_status(status: str | None) -> str:
    normalized_status = _normalize_optional_text(status)
    if normalized_status is None:
        return PendingStatus.OPEN
    if normalized_status not in _ALLOWED_STATUSES:
        allowed = ", ".join(sorted(_ALLOWED_STATUSES))
        raise BadRequestError(
            message=f"status must be one of: {allowed}.",
            code="INVALID_STATUS",
        )
    return normalized_status


def _validate_target_domain(target_domain: str | None) -> str | None:
    normalized_target_domain = _normalize_optional_text(target_domain)
    if normalized_target_domain is None:
        return None
    if normalized_target_domain not in _ALLOWED_TARGET_DOMAINS:
        allowed = ", ".join(sorted(_ALLOWED_TARGET_DOMAINS))
        raise BadRequestError(
            message=f"target_domain must be one of: {allowed}.",
            code="INVALID_TARGET_DOMAIN",
        )
    return normalized_target_domain


def _validate_date_range(*, date_from: datetime | None, date_to: datetime | None) -> None:
    if date_from is not None and date_to is not None and date_from > date_to:
        raise BadRequestError(
            message="date_from must be earlier than or equal to date_to.",
            code="INVALID_DATE_RANGE",
        )


def fix_pending_item(
    db: Session,
    *,
    pending_item_id: int,
    corrected_payload_json: JsonValue = None,
    note: str | None = None,
) -> PendingItem:
    try:
        pending_item = _get_pending_item_or_raise(db, pending_item_id)
        _ensure_pending_is_actionable(pending_item)

        before_payload = _get_latest_payload_snapshot(pending_item)
        repository.update_pending_corrected_payload(
            db,
            pending_item=pending_item,
            corrected_payload_json=corrected_payload_json,
        )
        repository.create_pending_review_action(
            db,
            pending_item_id=pending_item.id,
            action_type=PendingActionType.FIX,
            before_payload_json=before_payload,
            after_payload_json=corrected_payload_json,
            note=note,
        )

        db.commit()
        db.refresh(pending_item)
        return pending_item
    except Exception:
        db.rollback()
        raise


def confirm_pending_item(
    db: Session,
    *,
    pending_item_id: int,
    note: str | None = None,
) -> PendingItem:
    try:
        pending_item = _get_pending_item_or_raise(db, pending_item_id)
        _ensure_pending_is_actionable(pending_item)
        capture = _get_capture_for_pending_or_raise(db, pending_item)
        effective_payload = _select_effective_payload(pending_item)

        _write_fact_record(
            db,
            pending_item=pending_item,
            capture=capture,
            payload=effective_payload,
        )

        resolved_at = datetime.utcnow()
        repository.update_pending_status(
            db,
            pending_item=pending_item,
            status=PendingStatus.CONFIRMED,
        )
        repository.set_pending_resolved_at(
            db,
            pending_item=pending_item,
            resolved_at=resolved_at,
        )
        capture.status = CaptureStatus.COMMITTED
        capture.finalized_at = resolved_at
        db.flush()

        repository.create_pending_review_action(
            db,
            pending_item_id=pending_item.id,
            action_type=PendingActionType.CONFIRM,
            before_payload_json=effective_payload,
            after_payload_json=effective_payload,
            note=note,
        )

        db.commit()
        db.refresh(pending_item)
        return pending_item
    except Exception:
        db.rollback()
        raise


def discard_pending_item(
    db: Session,
    *,
    pending_item_id: int,
    note: str | None = None,
) -> PendingItem:
    try:
        pending_item = _get_pending_item_or_raise(db, pending_item_id)
        _ensure_pending_is_actionable(pending_item)
        capture = _get_capture_for_pending_or_raise(db, pending_item)

        resolved_at = datetime.utcnow()
        repository.update_pending_status(
            db,
            pending_item=pending_item,
            status=PendingStatus.DISCARDED,
        )
        repository.set_pending_resolved_at(
            db,
            pending_item=pending_item,
            resolved_at=resolved_at,
        )
        capture.status = CaptureStatus.DISCARDED
        capture.finalized_at = resolved_at
        db.flush()

        repository.create_pending_review_action(
            db,
            pending_item_id=pending_item.id,
            action_type=PendingActionType.DISCARD,
            before_payload_json=_get_latest_payload_snapshot(pending_item),
            after_payload_json=None,
            note=note,
        )

        db.commit()
        db.refresh(pending_item)
        return pending_item
    except Exception:
        db.rollback()
        raise


def force_insert_pending_item(
    db: Session,
    *,
    pending_item_id: int,
    note: str | None = None,
) -> PendingItem:
    try:
        pending_item = _get_pending_item_or_raise(db, pending_item_id)
        _ensure_pending_is_actionable(pending_item)
        capture = _get_capture_for_pending_or_raise(db, pending_item)
        effective_payload = _select_effective_payload(pending_item)

        _write_fact_record(
            db,
            pending_item=pending_item,
            capture=capture,
            payload=effective_payload,
        )

        resolved_at = datetime.utcnow()
        repository.update_pending_status(
            db,
            pending_item=pending_item,
            status=PendingStatus.FORCED,
        )
        repository.set_pending_resolved_at(
            db,
            pending_item=pending_item,
            resolved_at=resolved_at,
        )
        capture.status = CaptureStatus.COMMITTED
        capture.finalized_at = resolved_at
        db.flush()

        repository.create_pending_review_action(
            db,
            pending_item_id=pending_item.id,
            action_type=PendingActionType.FORCE_INSERT,
            before_payload_json=effective_payload,
            after_payload_json=effective_payload,
            note=note,
        )

        db.commit()
        db.refresh(pending_item)
        return pending_item
    except Exception:
        db.rollback()
        raise


def add_review_action(
    db: Session,
    *,
    pending_item_id: int,
    action_type: str,
    before_payload_json: dict | list | str | int | float | bool | None = None,
    after_payload_json: dict | list | str | int | float | bool | None = None,
    note: str | None = None,
) -> PendingReviewAction:
    return repository.create_pending_review_action(
        db,
        pending_item_id=pending_item_id,
        action_type=action_type,
        before_payload_json=before_payload_json,
        after_payload_json=after_payload_json,
        note=note,
    )


def _get_pending_item_or_raise(db: Session, pending_item_id: int) -> PendingItem:
    pending_item = repository.get_pending_item_by_id(db, pending_item_id)
    if pending_item is None:
        raise NotFoundError(
            message=f"Pending item {pending_item_id} was not found.",
            code="PENDING_ITEM_NOT_FOUND",
        )
    return pending_item


def _get_capture_for_pending_or_raise(db: Session, pending_item: PendingItem) -> CaptureRecord:
    capture = capture_repository.get_capture_by_id(db, pending_item.capture_id)
    if capture is None:
        raise NotFoundError(
            message=f"Capture {pending_item.capture_id} for pending item {pending_item.id} was not found.",
            code="CAPTURE_NOT_FOUND",
        )
    return capture


def _ensure_pending_is_actionable(pending_item: PendingItem) -> None:
    if pending_item.status in ResolvedPendingStatuses:
        raise ConflictError(
            message=(
                f"Pending item {pending_item.id} is already resolved with status "
                f"{pending_item.status} and cannot be reviewed again."
            ),
            code="PENDING_ALREADY_RESOLVED",
        )
    if pending_item.status != PendingStatus.OPEN:
        raise ConflictError(
            message=(
                f"Pending item {pending_item.id} must be in status "
                f"{PendingStatus.OPEN} before review actions can be applied."
            ),
            code="PENDING_NOT_OPEN",
        )


def _select_effective_payload(pending_item: PendingItem) -> dict[str, Any]:
    validator = _get_payload_validator(pending_item.target_domain)

    corrected_payload = pending_item.corrected_payload_json
    if corrected_payload is not None:
        if validator(corrected_payload):
            return cast(dict[str, Any], corrected_payload)
        raise BadRequestError(
            message=(
                f"Pending item {pending_item.id} has corrected payload, "
                f"but it is invalid for target domain {pending_item.target_domain}."
            ),
            code="INVALID_CORRECTED_PAYLOAD",
        )

    proposed_payload = pending_item.proposed_payload_json
    if validator(proposed_payload):
        return cast(dict[str, Any], proposed_payload)

    raise BadRequestError(
        message=(
            f"Pending item {pending_item.id} does not have a valid payload for "
            f"target domain {pending_item.target_domain}."
        ),
        code="INVALID_PENDING_PAYLOAD",
    )


def _write_fact_record(
    db: Session,
    *,
    pending_item: PendingItem,
    capture: CaptureRecord,
    payload: dict[str, Any],
) -> None:
    if pending_item.target_domain == ParseTargetDomain.EXPENSE:
        create_expense_record(
            db,
            source_capture_id=capture.id,
            source_pending_id=pending_item.id,
            payload=payload,
        )
        return

    if pending_item.target_domain == ParseTargetDomain.KNOWLEDGE:
        create_knowledge_entry(
            db,
            source_capture_id=capture.id,
            source_pending_id=pending_item.id,
            payload=payload,
        )
        return

    if pending_item.target_domain == ParseTargetDomain.HEALTH:
        create_health_record(
            db,
            source_capture_id=capture.id,
            source_pending_id=pending_item.id,
            payload=payload,
        )
        return

    raise BadRequestError(
        message=f"Pending item {pending_item.id} has unsupported target domain {pending_item.target_domain}.",
        code="UNSUPPORTED_TARGET_DOMAIN",
    )


def _get_payload_validator(target_domain: str) -> Any:
    if target_domain == ParseTargetDomain.EXPENSE:
        return _is_valid_expense_payload
    if target_domain == ParseTargetDomain.KNOWLEDGE:
        return _is_valid_knowledge_payload
    if target_domain == ParseTargetDomain.HEALTH:
        return _is_valid_health_payload
    raise BadRequestError(
        message=f"Unsupported target domain {target_domain}.",
        code="UNSUPPORTED_TARGET_DOMAIN",
    )


def _is_valid_expense_payload(payload: JsonValue) -> bool:
    payload_dict = _as_payload_dict(payload)
    if payload_dict is None:
        return False
    return bool(
        _string_value(payload_dict.get("amount"))
        and _string_value(payload_dict.get("currency"))
    )


def _is_valid_knowledge_payload(payload: JsonValue) -> bool:
    payload_dict = _as_payload_dict(payload)
    if payload_dict is None:
        return False
    return any(
        _string_value(payload_dict.get(field_name))
        for field_name in ("title", "content", "source_text")
    )


def _is_valid_health_payload(payload: JsonValue) -> bool:
    payload_dict = _as_payload_dict(payload)
    if payload_dict is None:
        return False
    return bool(_string_value(payload_dict.get("metric_type")))


def _get_latest_payload_snapshot(pending_item: PendingItem) -> JsonValue:
    if pending_item.corrected_payload_json is not None:
        return pending_item.corrected_payload_json
    return pending_item.proposed_payload_json


def _as_payload_dict(payload: JsonValue) -> dict[str, Any] | None:
    if not isinstance(payload, dict):
        return None
    return payload


def _string_value(value: Any) -> str:
    if value is None:
        return ""
    return str(value).strip()
