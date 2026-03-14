from __future__ import annotations

from sqlalchemy.orm import Session

from app.domains.health.models import HealthRecord


def create_health_record(
    db: Session,
    *,
    source_capture_id: int,
    source_pending_id: int | None,
    payload: dict,
) -> HealthRecord:
    record = HealthRecord(
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        metric_type=str(payload.get("metric_type", "unknown")),
        value_text=payload.get("value_text"),
        note=payload.get("note"),
    )
    db.add(record)
    db.flush()
    return record