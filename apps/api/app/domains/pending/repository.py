from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import func
from sqlalchemy.orm import Session

from app.domains.pending.models import PendingItem, PendingReviewAction, PendingStatus


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None

_SORT_COLUMNS = {
    "created_at": PendingItem.created_at,
    "resolved_at": PendingItem.resolved_at,
    "status": func.lower(PendingItem.status),
    "target_domain": func.lower(PendingItem.target_domain),
}


def create_pending_item(
    db: Session,
    *,
    capture_id: int,
    parse_result_id: int,
    target_domain: str,
    proposed_payload_json: JsonValue = None,
    corrected_payload_json: JsonValue = None,
    reason: str | None = None,
    status: str = PendingStatus.OPEN,
) -> PendingItem:
    item = PendingItem(
        capture_id=capture_id,
        parse_result_id=parse_result_id,
        target_domain=target_domain,
        status=status,
        proposed_payload_json=proposed_payload_json,
        corrected_payload_json=corrected_payload_json,
        reason=reason,
    )
    db.add(item)
    db.flush()
    return item


def get_pending_item_by_id(db: Session, pending_item_id: int) -> PendingItem | None:
    return db.get(PendingItem, pending_item_id)


def list_pending_items(
    db: Session,
    *,
    page: int,
    page_size: int,
    sort_by: str,
    sort_order: str,
    status: str | None = None,
    target_domain: str | None = None,
    date_from: datetime | None = None,
    date_to: datetime | None = None,
) -> tuple[list[PendingItem], int]:
    query = db.query(PendingItem)

    if status is not None:
        query = query.filter(PendingItem.status == status)
    if target_domain is not None:
        query = query.filter(PendingItem.target_domain == target_domain)
    if date_from is not None:
        query = query.filter(PendingItem.created_at >= date_from)
    if date_to is not None:
        query = query.filter(PendingItem.created_at <= date_to)

    total = query.count()
    order_column = _SORT_COLUMNS[sort_by]
    order_clause = order_column.asc() if sort_order == "asc" else order_column.desc()
    items = (
        query.order_by(order_clause, PendingItem.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
        .all()
    )
    return items, total


def get_oldest_open_pending_item(db: Session) -> PendingItem | None:
    return (
        db.query(PendingItem)
        .filter(PendingItem.status == PendingStatus.OPEN)
        .order_by(PendingItem.created_at.asc(), PendingItem.id.asc())
        .first()
    )


def update_pending_corrected_payload(
    db: Session,
    *,
    pending_item: PendingItem,
    corrected_payload_json: JsonValue,
) -> PendingItem:
    pending_item.corrected_payload_json = corrected_payload_json
    db.flush()
    return pending_item


def update_pending_status(
    db: Session,
    *,
    pending_item: PendingItem,
    status: str,
) -> PendingItem:
    pending_item.status = status
    db.flush()
    return pending_item


def set_pending_resolved_at(
    db: Session,
    *,
    pending_item: PendingItem,
    resolved_at: datetime | None,
) -> PendingItem:
    pending_item.resolved_at = resolved_at
    db.flush()
    return pending_item


def create_pending_review_action(
    db: Session,
    *,
    pending_item_id: int,
    action_type: str,
    before_payload_json: JsonValue = None,
    after_payload_json: JsonValue = None,
    note: str | None = None,
) -> PendingReviewAction:
    action = PendingReviewAction(
        pending_item_id=pending_item_id,
        action_type=action_type,
        before_payload_json=before_payload_json,
        after_payload_json=after_payload_json,
        note=note,
    )
    db.add(action)
    db.flush()
    return action


def count_pending_items(
    db: Session,
    *,
    status: str | None = None,
    created_from: datetime | None = None,
    resolved_from: datetime | None = None,
    resolved_only: bool = False,
) -> int:
    query = db.query(PendingItem)

    if status is not None:
        query = query.filter(PendingItem.status == status)
    if created_from is not None:
        query = query.filter(PendingItem.created_at >= created_from)
    if resolved_from is not None:
        query = query.filter(PendingItem.resolved_at >= resolved_from)
    if resolved_only:
        query = query.filter(PendingItem.resolved_at.is_not(None))

    return query.count()


def count_pending_items_by_target_domain(
    db: Session,
    *,
    status: str | None = None,
) -> list[tuple[str, int]]:
    query = db.query(
        PendingItem.target_domain,
        func.count(PendingItem.id),
    )

    if status is not None:
        query = query.filter(PendingItem.status == status)

    rows = (
        query.group_by(PendingItem.target_domain)
        .order_by(PendingItem.target_domain.asc())
        .all()
    )
    return [(str(target_domain), int(count)) for target_domain, count in rows]


def list_recent_pending_review_actions(
    db: Session,
    *,
    limit: int,
) -> list[tuple[PendingReviewAction, PendingItem]]:
    return (
        db.query(PendingReviewAction, PendingItem)
        .join(PendingItem, PendingItem.id == PendingReviewAction.pending_item_id)
        .order_by(PendingReviewAction.created_at.desc(), PendingReviewAction.id.desc())
        .limit(limit)
        .all()
    )
