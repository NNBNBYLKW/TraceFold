from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP
import random
from typing import Any

from sqlalchemy import func
from sqlalchemy.orm import Session

from app.domains.ai_derivations import repository as ai_derivations_repository
from app.domains.ai_derivations.models import AiDerivation
from app.domains.ai_derivations.service import (
    FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
    FORMAL_TARGET_TYPE_KNOWLEDGE,
)
from app.domains.alerts.models import RuleAlert
from app.domains.capture import service as capture_service
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseResult
from app.domains.expense.models import ExpenseRecord
from app.domains.expense.service import create_expense_record
from app.domains.health.models import HealthRecord
from app.domains.health.service import create_health_record, rerun_health_rules
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.knowledge.service import (
    create_knowledge_entry,
    request_knowledge_summary_recompute,
)
from app.domains.pending.models import PendingItem, PendingReviewAction
from app.domains.system_tasks.models import TaskRun, TaskTriggerSource
from app.domains.system_tasks.service import execute_task_now, request_task_run
from app.domains.workbench.models import WorkbenchRecentContext
from app.tasks.registry import (
    TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
    TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE,
)


SEED_CAPTURE_SOURCE_TYPE = "seed_demo"
SEED_CAPTURE_MARKER = "tracefold_demo_seed_v1"
DEFAULT_EXPENSE_COUNT = 60
DEFAULT_KNOWLEDGE_COUNT = 30
DEFAULT_HEALTH_COUNT = 45

_EXPENSE_CATEGORY_SPECS = (
    {
        "category": "groceries",
        "keywords": ["weekly groceries", "fresh produce", "supermarket"],
        "notes": [
            "Weekly groceries run with fruit, vegetables, tofu, and yogurt.",
            "Supermarket restock for breakfast, noodles, and pantry basics.",
            "Fresh produce pickup before the weekend cooking plan.",
        ],
        "min_amount": Decimal("28.50"),
        "max_amount": Decimal("186.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "rent",
        "keywords": ["monthly rent", "apartment lease", "housing"],
        "notes": [
            "Monthly apartment rent transfer for the city flat.",
            "Housing payment for the current lease cycle.",
            "Rent transfer with building maintenance bundled in the note.",
        ],
        "min_amount": Decimal("3200.00"),
        "max_amount": Decimal("4200.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "transport",
        "keywords": ["metro top-up", "taxi", "bus card"],
        "notes": [
            "Metro top-up before the weekday commute block.",
            "Taxi ride after a late evening meetup downtown.",
            "Bus card recharge with extra balance for the month.",
        ],
        "min_amount": Decimal("6.00"),
        "max_amount": Decimal("128.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "utilities",
        "keywords": ["electricity bill", "water bill", "internet"],
        "notes": [
            "Utility payment covering electricity and water this month.",
            "Home internet bill settled for the next billing cycle.",
            "Utilities bundle payment with a higher heating charge.",
        ],
        "min_amount": Decimal("48.00"),
        "max_amount": Decimal("380.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "entertainment",
        "keywords": ["movie night", "museum", "concert"],
        "notes": [
            "Movie night tickets and snacks for a weekend break.",
            "Museum pass picked up for a visiting friend.",
            "Concert ticket for a small live set in the neighborhood.",
        ],
        "min_amount": Decimal("35.00"),
        "max_amount": Decimal("460.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "eating_out",
        "keywords": ["lunch", "coffee", "dinner"],
        "notes": [
            "Lunch and coffee after a focused work session.",
            "Dinner with friends near the co-working space.",
            "Cafe visit with pour-over coffee and a sandwich.",
        ],
        "min_amount": Decimal("18.00"),
        "max_amount": Decimal("240.00"),
        "currencies": ("CNY",),
    },
    {
        "category": "shopping",
        "keywords": ["household goods", "desk setup", "stationery"],
        "notes": [
            "Household goods restock with detergent and paper supplies.",
            "Desk setup purchase with cables and a laptop stand.",
            "Stationery order for notebooks, pens, and sticky tabs.",
        ],
        "min_amount": Decimal("24.00"),
        "max_amount": Decimal("620.00"),
        "currencies": ("CNY", "USD"),
    },
    {
        "category": "subscription",
        "keywords": ["cloud storage", "music streaming", "newsletter"],
        "notes": [
            "Monthly cloud storage subscription renewal.",
            "Music streaming renewal with family plan pricing.",
            "Paid newsletter subscription for product strategy reading.",
        ],
        "min_amount": Decimal("12.00"),
        "max_amount": Decimal("168.00"),
        "currencies": ("CNY", "USD"),
    },
)

_KNOWLEDGE_TOPICS = (
    {
        "title_prefix": "SQLite migration note",
        "content_lead": "Summarized why migration revisions should stay the single schema source.",
        "source_prefix": "Migration checklist",
        "tags": ["sqlite", "migration", "schema", "baseline"],
    },
    {
        "title_prefix": "Workbench idea",
        "content_lead": "Captured a small product idea about keeping dashboard and workbench roles separate.",
        "source_prefix": "Workbench scratchpad",
        "tags": ["workbench", "dashboard", "ui", "entry"],
    },
    {
        "title_prefix": "Reading note",
        "content_lead": "Saved a reading excerpt and a short reflection to revisit later.",
        "source_prefix": "Book excerpt",
        "tags": ["reading", "notes", "excerpt", "reflection"],
    },
    {
        "title_prefix": "Project concept",
        "content_lead": "Outlined a lightweight project concept with next steps and open questions.",
        "source_prefix": "Project log",
        "tags": ["project", "idea", "next-steps", "planning"],
    },
    {
        "title_prefix": "Technical summary",
        "content_lead": "Condensed a technical reference into a smaller explanation for future reuse.",
        "source_prefix": "Technical source",
        "tags": ["technical", "summary", "api", "service"],
    },
    {
        "title_prefix": "Learning journal",
        "content_lead": "Recorded a learning checkpoint after trying a new pattern in the repo.",
        "source_prefix": "Learning log",
        "tags": ["learning", "journal", "pattern", "practice"],
    },
)

_HEALTH_METRIC_PATTERN = (
    ("heart_rate", "72 bpm", "Resting measurement after waking up."),
    ("sleep_duration", "455", "Sleep duration in minutes after a normal weeknight."),
    ("blood_pressure", "118/76", "Evening blood pressure reading after dinner."),
    ("heart_rate", "102 bpm", "Higher-than-usual heart rate after running for the train."),
    ("sleep_duration", "335", "Short sleep duration after a late project session."),
    ("blood_pressure", "144/92", "Elevated reading captured after a stressful day."),
    ("heart_rate", "134 bpm", "High heart rate during an intense workout cooldown."),
    ("sleep_duration", "282", "Very short sleep duration after travel and poor sleep."),
    ("blood_pressure", "182/122", "Very high reading flagged for immediate recheck."),
)


@dataclass(slots=True)
class DemoSeedOptions:
    expenses: int = DEFAULT_EXPENSE_COUNT
    knowledge_entries: int = DEFAULT_KNOWLEDGE_COUNT
    health_records: int = DEFAULT_HEALTH_COUNT
    random_seed: int = 20260323
    force: bool = False
    with_alerts: bool = False
    with_derivations: bool = False


@dataclass(slots=True)
class DemoSeedResult:
    expense_count: int
    knowledge_count: int
    health_count: int
    health_alert_count: int
    knowledge_derivation_count: int
    task_run_count: int
    dashboard_task_id: int
    derivation_task_ids: list[int]


def seed_demo_data(
    db: Session,
    *,
    options: DemoSeedOptions | None = None,
) -> DemoSeedResult:
    resolved_options = options or DemoSeedOptions()
    _validate_options(resolved_options)

    inspection = _inspect_existing_formal_data(db)
    if inspection["has_non_demo_formal_data"]:
        raise RuntimeError(
            "Seed data refused because the database already contains non-demo formal records. "
            "Use a fresh database instead of reseeding over real data."
        )
    if inspection["has_demo_formal_data"] and not resolved_options.force:
        raise RuntimeError(
            "Seed data refused because demo formal records already exist. "
            "Use --force to clear prior demo seed data and reseed."
        )
    if resolved_options.force and inspection["has_demo_formal_data"]:
        _reset_demo_seed_footprint(db)

    rng = random.Random(resolved_options.random_seed)
    created_records = _seed_formal_facts(db, resolved_options, rng)
    db.commit()

    if resolved_options.with_alerts:
        _rerun_health_alerts(db, health_ids=created_records["health_ids"])

    dashboard_task = request_task_run(
        db,
        task_type=TASK_TYPE_DASHBOARD_SUMMARY_REFRESH,
        trigger_source=TaskTriggerSource.MANUAL.value,
        payload_json={
            "seed_marker": SEED_CAPTURE_MARKER,
            "reason": "demo_seed_dashboard_refresh",
        },
    )
    execute_task_now(db, task_id=dashboard_task.id)

    derivation_task_ids: list[int] = []
    if resolved_options.with_derivations:
        for knowledge_id in created_records["knowledge_ids"][: min(5, len(created_records["knowledge_ids"]))]:
            task_payload = request_knowledge_summary_recompute(db, knowledge_id=knowledge_id)
            task = request_task_run(
                db,
                task_type=TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE,
                trigger_source=TaskTriggerSource.MANUAL.value,
                payload_json=task_payload,
            )
            execute_task_now(db, task_id=task.id)
            derivation_task_ids.append(task.id)

    return DemoSeedResult(
        expense_count=_count_rows(db, ExpenseRecord),
        knowledge_count=_count_rows(db, KnowledgeEntry),
        health_count=_count_rows(db, HealthRecord),
        health_alert_count=_count_rows(db, RuleAlert),
        knowledge_derivation_count=ai_derivations_repository.count_ai_derivations(
            db,
            target_type=FORMAL_TARGET_TYPE_KNOWLEDGE,
            derivation_kind=FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
        ),
        task_run_count=_count_rows(db, TaskRun),
        dashboard_task_id=dashboard_task.id,
        derivation_task_ids=derivation_task_ids,
    )


def _seed_formal_facts(
    db: Session,
    options: DemoSeedOptions,
    rng: random.Random,
) -> dict[str, list[int]]:
    now = _utcnow()
    expense_ids: list[int] = []
    knowledge_ids: list[int] = []
    health_ids: list[int] = []

    for index in range(options.expenses):
        created_at = _distributed_timestamp(
            index=index,
            total=max(options.expenses, 1),
            earliest=now - timedelta(days=175),
            latest=now - timedelta(hours=6),
            rng=rng,
        )
        expense_ids.append(_create_seed_expense(db, index=index, created_at=created_at, rng=rng).id)

    for index in range(options.knowledge_entries):
        created_at = _distributed_timestamp(
            index=index,
            total=max(options.knowledge_entries, 1),
            earliest=now - timedelta(days=145),
            latest=now - timedelta(hours=3),
            rng=rng,
        )
        knowledge_ids.append(_create_seed_knowledge(db, index=index, created_at=created_at).id)

    for index in range(options.health_records):
        created_at = _distributed_timestamp(
            index=index,
            total=max(options.health_records, 1),
            earliest=now - timedelta(days=95),
            latest=now - timedelta(hours=1),
            rng=rng,
        )
        health_ids.append(_create_seed_health(db, index=index, created_at=created_at, rng=rng).id)

    return {
        "expense_ids": expense_ids,
        "knowledge_ids": knowledge_ids,
        "health_ids": health_ids,
    }


def _create_seed_expense(
    db: Session,
    *,
    index: int,
    created_at: datetime,
    rng: random.Random,
) -> ExpenseRecord:
    spec = _EXPENSE_CATEGORY_SPECS[index % len(_EXPENSE_CATEGORY_SPECS)]
    amount = _random_decimal(rng, minimum=spec["min_amount"], maximum=spec["max_amount"])
    note = f"{spec['notes'][index % len(spec['notes'])]} Keyword: {spec['keywords'][index % len(spec['keywords'])]}."
    payload = {
        "amount": str(amount),
        "currency": spec["currencies"][rng.randrange(len(spec["currencies"]))],
        "category": spec["category"],
        "note": note,
    }
    capture = _create_seed_capture(
        db,
        domain="expense",
        index=index,
        created_at=created_at,
        raw_text=f"{payload['category']} expense {payload['amount']} {payload['currency']} {payload['note']}",
        raw_payload_json=payload,
    )
    record = create_expense_record(
        db,
        source_capture_id=capture.id,
        source_pending_id=None,
        payload=payload,
    )
    _apply_seed_timestamp(record, created_at=created_at)
    return record


def _create_seed_knowledge(
    db: Session,
    *,
    index: int,
    created_at: datetime,
) -> KnowledgeEntry:
    topic = _KNOWLEDGE_TOPICS[index % len(_KNOWLEDGE_TOPICS)]
    ordinal = index + 1
    title = f"{topic['title_prefix']} #{ordinal}"
    content = (
        f"{topic['content_lead']} "
        f"Key focus areas include {topic['tags'][0]}, {topic['tags'][1]}, and a small note on {topic['tags'][2]}. "
        f"This entry keeps enough detail for list, detail, and knowledge_summary smoke validation."
    )
    if index % 4 == 0:
        content += " A longer follow-up paragraph records tradeoffs, implementation notes, and a small next-step checklist."
    source_text = (
        f"{topic['source_prefix']} #{ordinal}: "
        f"Saved snippet about {topic['tags'][0]}, {topic['tags'][1]}, and {topic['tags'][3]} for later recall."
    )
    if index % 5 == 0:
        source_text = None
    payload = {
        "title": title if index % 9 != 0 else None,
        "content": content,
        "source_text": source_text,
    }
    capture = _create_seed_capture(
        db,
        domain="knowledge",
        index=index,
        created_at=created_at,
        raw_text=f"{title}. {content}",
        raw_payload_json=payload,
    )
    entry = create_knowledge_entry(
        db,
        source_capture_id=capture.id,
        source_pending_id=None,
        payload=payload,
    )
    _apply_seed_timestamp(entry, created_at=created_at)
    return entry


def _create_seed_health(
    db: Session,
    *,
    index: int,
    created_at: datetime,
    rng: random.Random,
) -> HealthRecord:
    metric_type, value_text, note = _HEALTH_METRIC_PATTERN[index % len(_HEALTH_METRIC_PATTERN)]
    payload = {
        "metric_type": metric_type,
        "value_text": value_text,
        "note": f"{note} Log window {1 + (index % 6)}. Keyword: {_health_keyword(metric_type, index, rng)}.",
    }
    capture = _create_seed_capture(
        db,
        domain="health",
        index=index,
        created_at=created_at,
        raw_text=f"{metric_type} {value_text}. {payload['note']}",
        raw_payload_json=payload,
    )
    record = create_health_record(
        db,
        source_capture_id=capture.id,
        source_pending_id=None,
        payload=payload,
    )
    _apply_seed_timestamp(record, created_at=created_at)
    return record


def _create_seed_capture(
    db: Session,
    *,
    domain: str,
    index: int,
    created_at: datetime,
    raw_text: str,
    raw_payload_json: dict[str, Any],
) -> CaptureRecord:
    capture = capture_service.submit_capture(
        db,
        source_type=SEED_CAPTURE_SOURCE_TYPE,
        source_ref=f"{SEED_CAPTURE_MARKER}:{domain}:{index + 1:04d}",
        raw_text=raw_text,
        raw_payload_json={
            "seed_marker": SEED_CAPTURE_MARKER,
            "domain": domain,
            "payload": raw_payload_json,
        },
    )
    capture.status = CaptureStatus.COMMITTED
    capture.finalized_at = created_at
    _apply_seed_timestamp(capture, created_at=created_at)
    return capture


def _rerun_health_alerts(db: Session, *, health_ids: list[int]) -> None:
    for health_id in health_ids:
        rerun_health_rules(db, health_id=health_id)


def _reset_demo_seed_footprint(db: Session) -> None:
    db.query(AiDerivation).delete(synchronize_session=False)
    db.query(RuleAlert).delete(synchronize_session=False)
    db.query(TaskRun).delete(synchronize_session=False)
    db.query(WorkbenchRecentContext).delete(synchronize_session=False)
    db.query(ExpenseRecord).delete(synchronize_session=False)
    db.query(KnowledgeEntry).delete(synchronize_session=False)
    db.query(HealthRecord).delete(synchronize_session=False)
    db.query(ParseResult).delete(synchronize_session=False)
    db.query(PendingReviewAction).delete(synchronize_session=False)
    db.query(PendingItem).delete(synchronize_session=False)
    (
        db.query(CaptureRecord)
        .filter(CaptureRecord.source_type == SEED_CAPTURE_SOURCE_TYPE)
        .delete(synchronize_session=False)
    )
    db.commit()


def _inspect_existing_formal_data(db: Session) -> dict[str, bool]:
    formal_total = (
        _count_rows(db, ExpenseRecord)
        + _count_rows(db, KnowledgeEntry)
        + _count_rows(db, HealthRecord)
    )
    demo_total = (
        _count_demo_formal_rows(db, ExpenseRecord)
        + _count_demo_formal_rows(db, KnowledgeEntry)
        + _count_demo_formal_rows(db, HealthRecord)
    )
    return {
        "has_demo_formal_data": demo_total > 0,
        "has_non_demo_formal_data": formal_total > demo_total,
    }


def _count_demo_formal_rows(
    db: Session,
    model: type[ExpenseRecord] | type[KnowledgeEntry] | type[HealthRecord],
) -> int:
    return int(
        db.query(func.count(model.id))
        .join(CaptureRecord, model.source_capture_id == CaptureRecord.id)
        .filter(CaptureRecord.source_type == SEED_CAPTURE_SOURCE_TYPE)
        .scalar()
        or 0
    )


def _count_rows(db: Session, model: Any) -> int:
    return int(db.query(func.count("*")).select_from(model).scalar() or 0)


def _validate_options(options: DemoSeedOptions) -> None:
    for field_name in ("expenses", "knowledge_entries", "health_records"):
        if getattr(options, field_name) < 0:
            raise ValueError(f"{field_name} must be greater than or equal to 0.")


def _distributed_timestamp(
    *,
    index: int,
    total: int,
    earliest: datetime,
    latest: datetime,
    rng: random.Random,
) -> datetime:
    if total <= 1:
        return latest
    total_span = latest - earliest
    ratio = index / max(total - 1, 1)
    created_at = earliest + timedelta(seconds=total_span.total_seconds() * ratio)
    created_at += timedelta(minutes=rng.randint(0, 180))
    if created_at > latest:
        return latest - timedelta(minutes=index % 60)
    return created_at


def _apply_seed_timestamp(record: Any, *, created_at: datetime) -> None:
    if hasattr(record, "created_at"):
        record.created_at = created_at
    if hasattr(record, "updated_at"):
        record.updated_at = created_at


def _random_decimal(
    rng: random.Random,
    *,
    minimum: Decimal,
    maximum: Decimal,
) -> Decimal:
    cents = rng.randint(int(minimum * 100), int(maximum * 100))
    return (Decimal(cents) / Decimal("100")).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def _health_keyword(metric_type: str, index: int, rng: random.Random) -> str:
    keywords_by_metric = {
        "heart_rate": ("morning check", "exercise recovery", "commute spike"),
        "sleep_duration": ("sleep debt", "weeknight recovery", "travel fatigue"),
        "blood_pressure": ("resting check", "stress spike", "follow-up reading"),
    }
    choices = keywords_by_metric.get(metric_type, ("health log",))
    return choices[(index + rng.randint(0, len(choices) - 1)) % len(choices)]


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
