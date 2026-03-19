from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture.models import CaptureRecord, CaptureStatus, ParseConfidenceLevel, ParseResult, ParseTargetDomain
from app.domains.expense.models import ExpenseRecord
from app.domains.health.models import HealthRecord
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.pending.models import PendingItem, PendingStatus
from app.domains.workbench.models import WorkbenchRecentContext
from app.domains.workbench import service as workbench_service
from app.main import app


@pytest.fixture
def db(tmp_path: Path) -> Generator[Session, None, None]:
    import app.domains.alerts.models  # noqa: F401
    import app.domains.ai_derivations.models  # noqa: F401
    import app.domains.capture.models  # noqa: F401
    import app.domains.expense.models  # noqa: F401
    import app.domains.health.models  # noqa: F401
    import app.domains.knowledge.models  # noqa: F401
    import app.domains.pending.models  # noqa: F401
    import app.domains.system_tasks.models  # noqa: F401
    import app.domains.workbench.models  # noqa: F401

    engine = create_engine(
        f"sqlite:///{tmp_path / 'workbench-api.db'}",
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
def api_client(
    db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        yield db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def _create_capture(
    db: Session,
    *,
    raw_text: str,
    status: str = CaptureStatus.COMMITTED,
) -> CaptureRecord:
    capture = CaptureRecord(
        source_type="test",
        source_ref="seed",
        raw_text=raw_text,
        status=status,
    )
    db.add(capture)
    db.flush()
    return capture


def _create_parse_result(
    db: Session,
    *,
    capture: CaptureRecord,
    target_domain: str,
    parsed_payload_json: dict[str, object],
) -> ParseResult:
    parse_result = ParseResult(
        capture_id=capture.id,
        target_domain=target_domain,
        confidence_score=0.5,
        confidence_level=ParseConfidenceLevel.MEDIUM,
        parsed_payload_json=parsed_payload_json,
        parser_name="test",
        parser_version="1",
    )
    db.add(parse_result)
    db.flush()
    return parse_result


def _seed_formal_records(db: Session) -> tuple[ExpenseRecord, KnowledgeEntry, HealthRecord]:
    expense_capture = _create_capture(db, raw_text="expense formal", status=CaptureStatus.COMMITTED)
    knowledge_capture = _create_capture(db, raw_text="knowledge formal", status=CaptureStatus.COMMITTED)
    health_capture = _create_capture(db, raw_text="health formal", status=CaptureStatus.COMMITTED)

    expense = ExpenseRecord(
        source_capture_id=expense_capture.id,
        source_pending_id=None,
        amount="25",
        currency="CNY",
        category="food",
        note="Lunch",
    )
    knowledge = KnowledgeEntry(
        source_capture_id=knowledge_capture.id,
        source_pending_id=None,
        title="Knowledge note",
        content="Useful knowledge",
        source_text="Useful knowledge",
    )
    health = HealthRecord(
        source_capture_id=health_capture.id,
        source_pending_id=None,
        metric_type="weight",
        value_text="70kg",
        note="Morning",
    )
    db.add_all([expense, knowledge, health])
    db.commit()
    db.refresh(expense)
    db.refresh(knowledge)
    db.refresh(health)
    return expense, knowledge, health


def _seed_pending_item(db: Session) -> PendingItem:
    capture = _create_capture(db, raw_text="买了咖啡", status=CaptureStatus.PENDING)
    parse_result = _create_parse_result(
        db,
        capture=capture,
        target_domain=ParseTargetDomain.EXPENSE,
        parsed_payload_json={"amount": "18", "currency": "CNY", "category": "food"},
    )
    pending_item = PendingItem(
        capture_id=capture.id,
        parse_result_id=parse_result.id,
        target_domain=ParseTargetDomain.EXPENSE,
        status=PendingStatus.OPEN,
        proposed_payload_json={"amount": "18", "currency": "CNY", "category": "food"},
        corrected_payload_json=None,
        reason="needs review",
    )
    db.add(pending_item)
    db.commit()
    db.refresh(pending_item)
    return pending_item


def test_workbench_home_returns_builtin_sections(api_client: TestClient) -> None:
    response = api_client.get("/api/workbench/home")

    assert response.status_code == 200
    body = response.json()
    assert body["success"] is True
    data = body["data"]
    assert data["current_mode"]["template_id"] is not None
    assert len(data["templates"]) >= 3
    assert data["pinned_shortcuts"] == []
    assert data["recent_contexts"] == []
    assert "pending_summary" in data["dashboard_summary"]
    assert "alert_summary" in data["dashboard_summary"]


def test_workbench_template_shortcut_preferences_and_apply_flow(
    api_client: TestClient,
    db: Session,
) -> None:
    shortcut_response = api_client.post(
        "/api/workbench/shortcuts",
        json={
            "label": "Food expenses",
            "target_type": "module_view",
            "target_payload_json": {
                "module": "expense",
                "view_key": "list",
                "query": {"category": "food", "sort_by": "created_at", "sort_order": "desc"},
            },
            "sort_order": 5,
            "is_enabled": True,
        },
    )
    assert shortcut_response.status_code == 201
    shortcut_id = shortcut_response.json()["data"]["shortcut_id"]

    patch_shortcut_response = api_client.patch(
        f"/api/workbench/shortcuts/{shortcut_id}",
        json={"label": "Recent food expenses"},
    )
    assert patch_shortcut_response.status_code == 200
    assert patch_shortcut_response.json()["data"]["label"] == "Recent food expenses"

    template_response = api_client.post(
        "/api/workbench/templates",
        json={
            "template_type": "user",
            "name": "Food Review",
            "default_module": "expense",
            "default_view_key": "list",
            "default_query_json": {"category": "food", "sort_by": "created_at", "sort_order": "desc"},
            "description": "Focus on recent food expenses.",
            "scoped_shortcut_ids": [shortcut_id],
            "sort_order": 40,
            "is_enabled": True,
        },
    )
    assert template_response.status_code == 201
    template_id = template_response.json()["data"]["template_id"]

    update_template_response = api_client.patch(
        f"/api/workbench/templates/{template_id}",
        json={"description": "Updated food review mode."},
    )
    assert update_template_response.status_code == 200
    assert update_template_response.json()["data"]["description"] == "Updated food review mode."

    before_fact_counts = (
        db.query(ExpenseRecord).count(),
        db.query(KnowledgeEntry).count(),
        db.query(HealthRecord).count(),
    )

    apply_response = api_client.post(
        f"/api/workbench/templates/{template_id}/apply",
        json={"set_as_default": True},
    )
    assert apply_response.status_code == 200
    apply_data = apply_response.json()["data"]
    assert apply_data["template_applied"] is True
    assert apply_data["active_template_id"] == template_id
    assert apply_data["default_template_id"] == template_id

    preferences_response = api_client.get("/api/workbench/preferences")
    assert preferences_response.status_code == 200
    preferences_data = preferences_response.json()["data"]
    assert preferences_data["active_template_id"] == template_id
    assert preferences_data["default_template_id"] == template_id

    home_response = api_client.get("/api/workbench/home")
    home_data = home_response.json()["data"]
    assert home_data["current_mode"]["template_id"] == template_id
    assert [item["shortcut_id"] for item in home_data["pinned_shortcuts"]] == [shortcut_id]

    after_fact_counts = (
        db.query(ExpenseRecord).count(),
        db.query(KnowledgeEntry).count(),
        db.query(HealthRecord).count(),
    )
    assert after_fact_counts == before_fact_counts


def test_workbench_shortcut_can_be_deleted(api_client: TestClient) -> None:
    create_response = api_client.post(
        "/api/workbench/shortcuts",
        json={
            "label": "Pending queue",
            "target_type": "route",
            "target_payload_json": {"route": "/pending"},
        },
    )
    shortcut_id = create_response.json()["data"]["shortcut_id"]

    delete_response = api_client.delete(f"/api/workbench/shortcuts/{shortcut_id}")
    assert delete_response.status_code == 204

    list_response = api_client.get("/api/workbench/shortcuts")
    assert list_response.status_code == 200
    assert all(item["shortcut_id"] != shortcut_id for item in list_response.json()["data"]["items"])


def test_workbench_shortcuts_are_returned_in_sort_order(api_client: TestClient) -> None:
    response_a = api_client.post(
        "/api/workbench/shortcuts",
        json={
            "label": "Late",
            "target_type": "route",
            "target_payload_json": {"route": "/expense"},
            "sort_order": 30,
        },
    )
    response_b = api_client.post(
        "/api/workbench/shortcuts",
        json={
            "label": "Early",
            "target_type": "route",
            "target_payload_json": {"route": "/pending"},
            "sort_order": 10,
        },
    )

    assert response_a.status_code == 201
    assert response_b.status_code == 201

    list_response = api_client.get("/api/workbench/shortcuts")
    labels = [item["label"] for item in list_response.json()["data"]["items"]]

    assert labels == ["Early", "Late"]


def test_workbench_rejects_invalid_query_and_invalid_preference_template(api_client: TestClient) -> None:
    invalid_query_response = api_client.post(
        "/api/workbench/templates",
        json={
            "template_type": "user",
            "name": "Bad template",
            "default_module": "expense",
            "default_query_json": {"action": "confirm"},
        },
    )
    assert invalid_query_response.status_code == 400
    assert invalid_query_response.json()["error"]["code"] == "INVALID_TEMPLATE_DEFAULT_QUERY"

    invalid_preference_response = api_client.patch(
        "/api/workbench/preferences",
        json={"active_template_id": 99999},
    )
    assert invalid_preference_response.status_code == 404
    assert invalid_preference_response.json()["error"]["code"] == "WORKBENCH_TEMPLATE_NOT_FOUND"


def test_workbench_preferences_can_be_read_and_updated(api_client: TestClient) -> None:
    template_response = api_client.post(
        "/api/workbench/templates",
        json={
            "template_type": "user",
            "name": "Health review",
            "default_module": "health",
            "default_view_key": "list",
            "default_query_json": {"sort_by": "created_at", "sort_order": "desc"},
        },
    )
    template_id = template_response.json()["data"]["template_id"]

    update_response = api_client.patch(
        "/api/workbench/preferences",
        json={
            "active_template_id": template_id,
            "default_template_id": template_id,
        },
    )

    assert update_response.status_code == 200
    assert update_response.json()["data"]["active_template_id"] == template_id
    assert update_response.json()["data"]["default_template_id"] == template_id

    read_response = api_client.get("/api/workbench/preferences")
    assert read_response.status_code == 200
    assert read_response.json()["data"]["active_template_id"] == template_id


def test_workbench_recent_records_views_and_pending_actions(
    api_client: TestClient,
    db: Session,
) -> None:
    expense, knowledge, health = _seed_formal_records(db)
    pending_for_fix = _seed_pending_item(db)
    pending_for_confirm = _seed_pending_item(db)

    assert api_client.get(f"/api/expense/{expense.id}").status_code == 200
    assert api_client.get(f"/api/knowledge/{knowledge.id}").status_code == 200
    assert api_client.get(f"/api/health/{health.id}").status_code == 200
    assert api_client.get(f"/api/pending/{pending_for_fix.id}").status_code == 200

    fix_response = api_client.post(
        f"/api/pending/{pending_for_fix.id}/fix",
        json={"correction_text": "30元午饭"},
    )
    assert fix_response.status_code == 200

    confirm_response = api_client.post(
        f"/api/pending/{pending_for_confirm.id}/confirm",
        json={"note": "looks good"},
    )
    assert confirm_response.status_code == 200

    recent_response = api_client.get("/api/workbench/recent")
    assert recent_response.status_code == 200
    recent_data = recent_response.json()["data"]
    assert recent_data["limit"] == 5
    assert recent_data["total"] == 6
    assert len(recent_data["items"]) == 5

    pending_entries = [item for item in recent_data["items"] if item["object_type"] == "pending"]
    acted_entries = [item for item in pending_entries if item["action_type"] == "acted"]

    assert any(
        item["object_id"] == str(pending_for_fix.id)
        and item["context_payload_json"]["status"] == "open"
        for item in acted_entries
    )
    assert any(
        item["object_id"] == str(pending_for_confirm.id)
        and item["context_payload_json"]["status"] == "confirmed"
        for item in acted_entries
    )
    assert any(
        item["object_id"] == str(pending_for_fix.id) and item["action_type"] == "viewed"
        for item in pending_entries
    )


def test_workbench_home_reuses_dashboard_summary_contract(api_client: TestClient) -> None:
    dashboard_response = api_client.get("/api/dashboard")
    home_response = api_client.get("/api/workbench/home")

    assert dashboard_response.status_code == 200
    assert home_response.status_code == 200
    assert home_response.json()["data"]["dashboard_summary"] == dashboard_response.json()["data"]
    assert "templates" not in home_response.json()["data"]["dashboard_summary"]


def test_workbench_recent_trims_to_twenty_and_returns_home_limit(db: Session, api_client: TestClient) -> None:
    for index in range(21):
        workbench_service.record_recent_context_best_effort(
            db,
            object_type="expense",
            object_id=str(index),
            action_type="viewed",
            title_snapshot=f"Expense {index}",
            route_snapshot=f"/expense/{index}",
            context_payload_json={"index": index},
        )

    assert db.query(WorkbenchRecentContext).count() == 20

    recent_response = api_client.get("/api/workbench/recent")
    assert recent_response.status_code == 200
    recent_data = recent_response.json()["data"]
    assert recent_data["limit"] == 5
    assert recent_data["total"] == 20
    assert len(recent_data["items"]) == 5
    assert recent_data["items"][0]["object_id"] == "20"
    assert recent_data["items"][-1]["object_id"] == "16"
