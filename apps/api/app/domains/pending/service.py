from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.pending import repository
from app.domains.pending.models import PendingItem, PendingReviewAction


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
