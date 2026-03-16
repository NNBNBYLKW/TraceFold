from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict


class PendingSummaryRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    open_count: int
    open_count_by_target_domain: dict[str, int]
    opened_in_last_7_days: int
    resolved_in_last_7_days: int
    href: str


class QuickLinkRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    label: str
    href: str


class ExpenseSummaryRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    created_in_current_month: int
    amount_by_currency_current_month: dict[str, str]
    latest_expense_created_at: datetime | None = None
    href: str


class KnowledgeSummaryRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    created_in_last_7_days: int
    created_in_last_30_days: int
    latest_knowledge_created_at: datetime | None = None
    href: str


class HealthSummaryRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    created_in_last_7_days: int
    latest_health_created_at: datetime | None = None
    recent_metric_types: list[str]
    href: str


class RecentActivityRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    activity_type: str
    occurred_at: datetime
    target_domain: str
    target_id: int
    title_or_preview: str | None = None
    action_label: str
    href: str


class DashboardRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    pending_summary: PendingSummaryRead
    quick_links: list[QuickLinkRead]
    expense_summary: ExpenseSummaryRead
    knowledge_summary: KnowledgeSummaryRead
    health_summary: HealthSummaryRead
    recent_activity: list[RecentActivityRead]
