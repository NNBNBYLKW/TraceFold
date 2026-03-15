from __future__ import annotations

from collections.abc import Generator
from datetime import datetime, timedelta
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.exceptions import NotFoundError
from app.db.base import Base
from app.db.session import get_db
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus, ParseConfidenceLevel, ParseTargetDomain
from app.domains.expense.service import create_expense_record, get_expense_read, list_expense_reads
from app.domains.health.service import create_health_record, get_health_read, list_health_reads
from app.domains.knowledge.service import create_knowledge_entry, get_knowledge_read, list_knowledge_reads
from app.domains.pending import repository as pending_repository
from app.main import app


@pytest.fixture
def db(tmp_path: Path) -> Generator[Session, None, None]:
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'formal-read-layer.db'}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    testing_session_local = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )

    Base.metadata.create_all(bind=engine)
    session = testing_session_local()
    try:
        yield session
    finally:
        session.close()
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


@pytest.fixture
def api_client(db: Session) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        yield db

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_expense_list_service_supports_default_sort_category_and_note_keyword(db: Session) -> None:
    oldest = _create_expense_fact(
        db,
        created_at=_dt(minutes=1),
        amount="8.00",
        category="transport",
        note="bus ride",
        with_pending=False,
    )
    middle = _create_expense_fact(
        db,
        created_at=_dt(minutes=2),
        amount="12.50",
        category="food",
        note="lunch keyword match",
    )
    newest = _create_expense_fact(
        db,
        created_at=_dt(minutes=3),
        amount="25.00",
        category="food",
        note="team dinner",
    )

    result = list_expense_reads(db)
    filtered_by_category = list_expense_reads(db, category="food")
    filtered_by_keyword = list_expense_reads(db, keyword="keyword")

    assert [item.amount for item in result.items] == [newest.amount, middle.amount, oldest.amount]
    assert [item.id for item in result.items] == [newest.id, middle.id, oldest.id]
    assert [item.category for item in filtered_by_category.items] == ["food", "food"]
    assert [item.amount for item in filtered_by_keyword.items] == [middle.amount]
    assert filtered_by_keyword.items[0].note_preview == "lunch keyword match"


def test_knowledge_list_service_handles_untitled_keyword_scope_and_source_text_flag(db: Session) -> None:
    untitled = _create_knowledge_fact(
        db,
        created_at=_dt(minutes=1),
        title="   ",
        content="formal knowledge body",
        source_text="   ",
        with_pending=False,
    )
    titled = _create_knowledge_fact(
        db,
        created_at=_dt(minutes=2),
        title="Alpha note",
        content="body text",
        source_text="secret source keyword",
    )

    result = list_knowledge_reads(db)
    title_match = list_knowledge_reads(db, keyword="Alpha")
    source_only_match = list_knowledge_reads(db, keyword="secret")

    assert result.items[1].display_title == "(untitled)"
    assert [item.id for item in result.items] == [titled.id, untitled.id]
    assert result.items[0].has_source_text is True
    assert result.items[1].has_source_text is False
    assert [item.display_title for item in title_match.items] == ["Alpha note"]
    assert source_only_match.items == []
    assert get_knowledge_read(db, untitled.id).title == "(untitled)"
    assert titled.source_pending_id is not None


def test_health_list_service_filters_note_keyword_and_metric_type_with_previews(db: Session) -> None:
    sleep = _create_health_fact(
        db,
        created_at=_dt(minutes=1),
        metric_type="sleep",
        value_text=None,
        note="slept 8 hours",
        with_pending=False,
    )
    weight = _create_health_fact(
        db,
        created_at=_dt(minutes=2),
        metric_type="weight",
        value_text="70kg",
        note="morning keyword check-in",
    )

    keyword_match = list_health_reads(db, keyword="keyword")
    metric_match = list_health_reads(db, metric_type="sleep")
    all_items = list_health_reads(db)

    assert [item.metric_type for item in keyword_match.items] == ["weight"]
    assert [item.id for item in keyword_match.items] == [weight.id]
    assert [item.metric_type for item in metric_match.items] == ["sleep"]
    assert all_items.items[0].value_text_preview == "70kg"
    assert all_items.items[1].value_text_preview is None
    assert all_items.items[1].note_preview == "slept 8 hours"
    assert sleep.source_pending_id is None


@pytest.mark.parametrize(
    ("path", "domain"),
    [
        ("/api/expense/{id}", "expense"),
        ("/api/knowledge/{id}", "knowledge"),
        ("/api/health/{id}", "health"),
    ],
)
def test_detail_endpoints_return_expected_fields(
    api_client: TestClient,
    db: Session,
    path: str,
    domain: str,
) -> None:
    if domain == "expense":
        record = _create_expense_fact(
            db,
            created_at=_dt(minutes=1),
            amount="18.80",
            category="meal",
            note="client lunch",
        )
        expected_keys = {
            "id",
            "created_at",
            "amount",
            "currency",
            "category",
            "note",
            "source_capture_id",
            "source_pending_id",
        }
    elif domain == "knowledge":
        record = _create_knowledge_fact(
            db,
            created_at=_dt(minutes=1),
            title="   ",
            content="saved content",
            source_text="origin text",
        )
        expected_keys = {
            "id",
            "created_at",
            "title",
            "content",
            "source_text",
            "source_capture_id",
            "source_pending_id",
        }
    else:
        record = _create_health_fact(
            db,
            created_at=_dt(minutes=1),
            metric_type="blood_pressure",
            value_text="120/80",
            note="post-run",
        )
        expected_keys = {
            "id",
            "created_at",
            "metric_type",
            "value_text",
            "note",
            "source_capture_id",
            "source_pending_id",
        }

    response = api_client.get(path.format(id=record.id))

    assert response.status_code == 200
    payload = response.json()["data"]
    assert set(payload.keys()) == expected_keys
    if "title" in payload:
        assert payload["title"] == "(untitled)"


@pytest.mark.parametrize(
    ("getter", "record_id", "code"),
    [
        (get_expense_read, 999_001, "EXPENSE_NOT_FOUND"),
        (get_knowledge_read, 999_002, "KNOWLEDGE_NOT_FOUND"),
        (get_health_read, 999_003, "HEALTH_NOT_FOUND"),
    ],
)
def test_detail_services_raise_not_found_for_missing_records(
    db: Session,
    getter,
    record_id: int,
    code: str,
) -> None:
    with pytest.raises(NotFoundError) as exc_info:
        getter(db, record_id)

    assert exc_info.value.code == code


@pytest.mark.parametrize(
    ("path", "params", "code"),
    [
        ("/api/expense", {"sort_by": "invalid_field"}, "INVALID_SORT_BY"),
        ("/api/knowledge", {"sort_order": "sideways"}, "INVALID_SORT_ORDER"),
        ("/api/health", {"page_size": 101}, "INVALID_PAGE_SIZE"),
    ],
)
def test_list_endpoints_reject_invalid_parameters(
    api_client: TestClient,
    path: str,
    params: dict[str, object],
    code: str,
) -> None:
    response = api_client.get(path, params=params)

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == code


def _create_expense_fact(
    db: Session,
    *,
    created_at: datetime,
    amount: str,
    category: str | None,
    note: str | None,
    currency: str = "USD",
    with_pending: bool = True,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.EXPENSE,
        payload={"amount": amount, "currency": currency, "category": category, "note": note},
        with_pending=with_pending,
    )
    record = create_expense_record(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"amount": amount, "currency": currency, "category": category, "note": note},
    )
    record.created_at = created_at
    db.commit()
    return record


def _create_knowledge_fact(
    db: Session,
    *,
    created_at: datetime,
    title: str | None,
    content: str | None,
    source_text: str | None,
    with_pending: bool = True,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.KNOWLEDGE,
        payload={"title": title, "content": content, "source_text": source_text},
        with_pending=with_pending,
    )
    entry = create_knowledge_entry(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"title": title, "content": content, "source_text": source_text},
    )
    entry.created_at = created_at
    db.commit()
    return entry


def _create_health_fact(
    db: Session,
    *,
    created_at: datetime,
    metric_type: str,
    value_text: str | None,
    note: str | None,
    with_pending: bool = True,
):
    source_capture_id, source_pending_id = _create_lineage(
        db,
        target_domain=ParseTargetDomain.HEALTH,
        payload={"metric_type": metric_type, "value_text": value_text, "note": note},
        with_pending=with_pending,
    )
    record = create_health_record(
        db,
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        payload={"metric_type": metric_type, "value_text": value_text, "note": note},
    )
    record.created_at = created_at
    db.commit()
    return record


def _create_lineage(
    db: Session,
    *,
    target_domain: str,
    payload: dict[str, object | None],
    with_pending: bool,
) -> tuple[int, int | None]:
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        raw_text="fixture",
        status=CaptureStatus.COMMITTED,
    )
    parse_result = capture_repository.create_parse_result(
        db,
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.9,
        confidence_level=ParseConfidenceLevel.HIGH,
        parsed_payload_json=payload,
        parser_name="test",
        parser_version="0.1.0",
    )
    pending_id = None
    if with_pending:
        pending = pending_repository.create_pending_item(
            db,
            capture_id=capture.id,
            parse_result_id=parse_result.id,
            target_domain=target_domain,
            proposed_payload_json=payload,
            reason="fixture",
        )
        pending_id = pending.id
    db.flush()
    return capture.id, pending_id


def _dt(*, minutes: int) -> datetime:
    return datetime(2026, 1, 1, 12, 0, 0) + timedelta(minutes=minutes)
