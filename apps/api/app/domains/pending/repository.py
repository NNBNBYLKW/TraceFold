from __future__ import annotations

from typing import Any

from sqlalchemy.orm import Session

from app.domains.pending.models import PendingItem, PendingReviewAction, PendingStatus


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


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
