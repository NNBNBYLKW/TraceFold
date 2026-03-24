from __future__ import annotations

from datetime import UTC, datetime, timedelta
from decimal import Decimal, InvalidOperation
from typing import Any

from sqlalchemy.orm import Session

from app.core.logging import build_log_message, get_logger
from app.domains.alerts import repository as alerts_repository
from app.domains.alerts.models import AlertResult, AlertStatus
from app.domains.dashboard.schemas import (
    AlertSummaryItemRead,
    AlertSummaryRead,
    DashboardRead,
    ExpenseSummaryRead,
    HealthSummaryRead,
    KnowledgeSummaryRead,
    PendingSummaryRead,
    QuickLinkRead,
    RecentActivityRead,
)
from app.domains.expense import repository as expense_repository
from app.domains.expense.models import ExpenseRecord
from app.domains.health import repository as health_repository
from app.domains.health.models import HealthRecord
from app.domains.knowledge import repository as knowledge_repository
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingActionType, PendingItem, PendingReviewAction, PendingStatus


_PENDING_HREF = "/pending"
_EXPENSE_HREF = "/expense"
_KNOWLEDGE_HREF = "/knowledge"
_HEALTH_HREF = "/health"
_HEALTH_ALERTS_HREF = "/health?focus=alerts"
_RECENT_ACTIVITY_LIMIT = 10
_RECENT_METRIC_TYPES_LIMIT = 5
_RECENT_ALERTS_LIMIT = 5
_PREVIEW_LENGTH = 120
logger = get_logger(__name__)

_FORMAL_ACTIVITY_TYPE = "formal_record_created"
_PENDING_ACTIVITY_TYPE = "pending_review_action"

_PENDING_ACTION_LABELS = {
    PendingActionType.CONFIRM: "Confirmed pending item",
    PendingActionType.DISCARD: "Discarded pending item",
    PendingActionType.FIX: "Updated pending item",
    PendingActionType.FORCE_INSERT: "Forced pending insert",
}

_QUICK_LINKS = (
    QuickLinkRead(label="View pending items", href=_PENDING_HREF),
    QuickLinkRead(label="View expenses", href=_EXPENSE_HREF),
    QuickLinkRead(label="View knowledge", href=_KNOWLEDGE_HREF),
    QuickLinkRead(label="View health", href=_HEALTH_HREF),
)


def get_dashboard_read(
    db: Session,
    *,
    now: datetime | None = None,
    activity_limit: int = _RECENT_ACTIVITY_LIMIT,
) -> DashboardRead:
    try:
        reference_now = now or _utcnow()
        last_7_days = reference_now - timedelta(days=7)
        last_30_days = reference_now - timedelta(days=30)
        current_month_start = reference_now.replace(day=1, hour=0, minute=0, second=0, microsecond=0)

        expense_records_current_month = expense_repository.list_expense_records_created_between(
            db,
            created_from=current_month_start,
            created_to=reference_now,
        )
        latest_expense_record = _first_or_none(expense_repository.list_recent_expense_records(db, limit=1))
        latest_knowledge_entry = _first_or_none(knowledge_repository.list_recent_knowledge_entries(db, limit=1))
        latest_health_record = _first_or_none(health_repository.list_recent_health_records(db, limit=1))

        return DashboardRead(
            pending_summary=PendingSummaryRead(
                open_count=pending_repository.count_pending_items(db, status=PendingStatus.OPEN),
                open_count_by_target_domain=dict(
                    pending_repository.count_pending_items_by_target_domain(db, status=PendingStatus.OPEN)
                ),
                opened_in_last_7_days=pending_repository.count_pending_items(
                    db,
                    status=PendingStatus.OPEN,
                    created_from=last_7_days,
                ),
                resolved_in_last_7_days=pending_repository.count_pending_items(
                    db,
                    resolved_from=last_7_days,
                    resolved_only=True,
                ),
                href=_PENDING_HREF,
            ),
            alert_summary=AlertSummaryRead(
                open_count=alerts_repository.count_alert_results(
                    db,
                    source_domain="health",
                    status=AlertStatus.OPEN,
                ),
                recent_open_items=[
                    _build_alert_summary_item(result)
                    for result in alerts_repository.list_alert_results(
                        db,
                        source_domain="health",
                        status=AlertStatus.OPEN,
                        limit=_RECENT_ALERTS_LIMIT,
                    )
                ],
                href=_HEALTH_ALERTS_HREF,
            ),
            quick_links=list(_QUICK_LINKS),
            expense_summary=ExpenseSummaryRead(
                created_in_current_month=len(expense_records_current_month),
                amount_by_currency_current_month=_sum_expense_amounts_by_currency(expense_records_current_month),
                latest_expense_created_at=(
                    latest_expense_record.created_at if latest_expense_record is not None else None
                ),
                href=_EXPENSE_HREF,
            ),
            knowledge_summary=KnowledgeSummaryRead(
                created_in_last_7_days=knowledge_repository.count_knowledge_entries_created_since(
                    db,
                    created_from=last_7_days,
                ),
                created_in_last_30_days=knowledge_repository.count_knowledge_entries_created_since(
                    db,
                    created_from=last_30_days,
                ),
                latest_knowledge_created_at=(
                    latest_knowledge_entry.created_at if latest_knowledge_entry is not None else None
                ),
                href=_KNOWLEDGE_HREF,
            ),
            health_summary=HealthSummaryRead(
                created_in_last_7_days=health_repository.count_health_records_created_since(
                    db,
                    created_from=last_7_days,
                ),
                latest_health_created_at=latest_health_record.created_at if latest_health_record is not None else None,
                recent_metric_types=health_repository.list_recent_metric_types(
                    db,
                    limit=_RECENT_METRIC_TYPES_LIMIT,
                ),
                href=_HEALTH_HREF,
            ),
            recent_activity=_build_recent_activity(db, limit=activity_limit),
        )
    except Exception:
        logger.exception(
            build_log_message(
                "dashboard_summary_failed",
                domain="dashboard",
            )
        )
        raise


def build_dashboard_refresh_result(
    db: Session,
    *,
    now: datetime | None = None,
) -> dict[str, Any]:
    dashboard = get_dashboard_read(db, now=now)
    return {
        "pending_open_count": dashboard.pending_summary.open_count,
        "health_open_alert_count": dashboard.alert_summary.open_count,
        "expense_current_month_count": dashboard.expense_summary.created_in_current_month,
        "knowledge_last_7_days_count": dashboard.knowledge_summary.created_in_last_7_days,
        "health_last_7_days_count": dashboard.health_summary.created_in_last_7_days,
        "recent_activity_count": len(dashboard.recent_activity),
        "refreshed_at": _utcnow().isoformat(),
    }


def _build_recent_activity(
    db: Session,
    *,
    limit: int,
) -> list[RecentActivityRead]:
    activities = [
        *[
            _build_expense_activity(record)
            for record in expense_repository.list_recent_expense_records(db, limit=limit)
        ],
        *[
            _build_knowledge_activity(entry)
            for entry in knowledge_repository.list_recent_knowledge_entries(db, limit=limit)
        ],
        *[
            _build_health_activity(record)
            for record in health_repository.list_recent_health_records(db, limit=limit)
        ],
        *[
            _build_pending_action_activity(action, pending_item)
            for action, pending_item in pending_repository.list_recent_pending_review_actions(db, limit=limit)
        ],
    ]
    activities.sort(key=lambda activity: activity.occurred_at, reverse=True)
    return activities[:limit]


def _build_expense_activity(record: ExpenseRecord) -> RecentActivityRead:
    return RecentActivityRead(
        activity_type=_FORMAL_ACTIVITY_TYPE,
        occurred_at=record.created_at,
        target_domain="expense",
        target_id=record.id,
        title_or_preview=_normalize_optional_text(f"{record.amount} {record.currency}"),
        action_label="Created expense record",
        href=f"{_EXPENSE_HREF}/{record.id}",
    )


def _build_knowledge_activity(entry: KnowledgeEntry) -> RecentActivityRead:
    return RecentActivityRead(
        activity_type=_FORMAL_ACTIVITY_TYPE,
        occurred_at=entry.created_at,
        target_domain="knowledge",
        target_id=entry.id,
        title_or_preview=_build_knowledge_preview(entry.title, entry.content, entry.source_text),
        action_label="Created knowledge entry",
        href=f"{_KNOWLEDGE_HREF}/{entry.id}",
    )


def _build_health_activity(record: HealthRecord) -> RecentActivityRead:
    return RecentActivityRead(
        activity_type=_FORMAL_ACTIVITY_TYPE,
        occurred_at=record.created_at,
        target_domain="health",
        target_id=record.id,
        title_or_preview=_build_health_preview(record.metric_type, record.value_text, record.note),
        action_label="Created health record",
        href=f"{_HEALTH_HREF}/{record.id}",
    )


def _build_pending_action_activity(
    action: PendingReviewAction,
    pending_item: PendingItem,
) -> RecentActivityRead:
    return RecentActivityRead(
        activity_type=_PENDING_ACTIVITY_TYPE,
        occurred_at=action.created_at,
        target_domain=pending_item.target_domain,
        target_id=pending_item.id,
        title_or_preview=_build_pending_action_preview(action, pending_item.target_domain),
        action_label=_PENDING_ACTION_LABELS.get(action.action_type, "Reviewed pending item"),
        href=_PENDING_HREF,
    )


def _build_alert_summary_item(result: AlertResult) -> AlertSummaryItemRead:
    return AlertSummaryItemRead(
        id=result.id,
        source_record_id=result.source_record_id,
        severity=result.severity,
        title=result.title,
        message=result.message,
        triggered_at=result.triggered_at,
        href=f"{_HEALTH_HREF}/{result.source_record_id}",
    )


def _sum_expense_amounts_by_currency(records: list[ExpenseRecord]) -> dict[str, str]:
    totals: dict[str, Decimal] = {}

    for record in records:
        amount = _parse_decimal(record.amount)
        currency = _normalize_optional_text(record.currency)
        if amount is None or currency is None:
            continue
        totals[currency] = totals.get(currency, Decimal("0")) + amount

    return {
        currency: format(total, "f")
        for currency, total in sorted(totals.items(), key=lambda item: item[0])
    }


def _build_pending_action_preview(
    action: PendingReviewAction,
    target_domain: str,
) -> str | None:
    note = _normalize_optional_text(action.note)
    if note is not None:
        return note

    payload = action.after_payload_json
    if payload is None:
        payload = action.before_payload_json
    return _build_payload_preview(target_domain, payload)


def _build_payload_preview(
    target_domain: str,
    payload: Any,
) -> str | None:
    if not isinstance(payload, dict):
        return _build_preview(payload)

    if target_domain == "expense":
        return _build_expense_payload_preview(payload)
    if target_domain == "knowledge":
        return _build_knowledge_preview(
            payload.get("title"),
            payload.get("content"),
            payload.get("source_text"),
        )
    if target_domain == "health":
        return _build_health_preview(
            payload.get("metric_type"),
            payload.get("value_text"),
            payload.get("note"),
        )
    return _build_preview(payload)


def _build_expense_payload_preview(payload: dict[str, Any]) -> str | None:
    amount = _normalize_optional_text(payload.get("amount"))
    currency = _normalize_optional_text(payload.get("currency"))
    if amount and currency:
        return f"{amount} {currency}"
    return _build_preview(payload.get("note") or payload.get("category"))


def _build_knowledge_preview(
    title: Any,
    content: Any,
    source_text: Any,
) -> str | None:
    for candidate in (title, content, source_text):
        preview = _build_preview(candidate)
        if preview is not None:
            return preview
    return None


def _build_health_preview(
    metric_type: Any,
    value_text: Any,
    note: Any,
) -> str | None:
    metric = _normalize_optional_text(metric_type)
    value = _normalize_optional_text(value_text)
    if metric and value:
        return _truncate_preview(f"{metric}: {value}")
    if metric:
        return metric
    return _build_preview(note)


def _build_preview(value: Any) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    return _truncate_preview(normalized)


def _truncate_preview(value: str) -> str:
    if len(value) <= _PREVIEW_LENGTH:
        return value
    return value[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _parse_decimal(value: str | None) -> Decimal | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    try:
        amount = Decimal(normalized)
    except InvalidOperation:
        return None
    if not amount.is_finite():
        return None
    return amount


def _first_or_none(items: list[Any]) -> Any | None:
    return items[0] if items else None


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
