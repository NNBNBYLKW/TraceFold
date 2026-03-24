from __future__ import annotations

from typing import Any

from sqlalchemy.orm import Session

from app.domains.ai_derivations.service import execute_knowledge_summary_recompute_task
from app.domains.dashboard.service import build_dashboard_refresh_result


JsonValue = dict[str, Any] | list[Any] | str | int | float | bool | None


def run_dashboard_summary_refresh(
    db: Session,
    payload_json: JsonValue = None,
    task_id: int | None = None,
) -> dict[str, Any]:
    return build_dashboard_refresh_result(db)


def run_knowledge_summary_recompute(
    db: Session,
    payload_json: JsonValue = None,
    task_id: int | None = None,
) -> dict[str, Any]:
    return execute_knowledge_summary_recompute_task(
        db,
        payload_json,
        task_id=task_id,
    )
