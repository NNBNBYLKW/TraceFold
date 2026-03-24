from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.domains.capture import repository as capture_repository
from app.domains.capture.models import CaptureStatus
from app.domains.expense.models import ExpenseRecord
from app.domains.health.models import HealthRecord
from app.domains.health.service import create_health_record
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.knowledge.service import create_knowledge_entry
from app.domains.pending.models import PendingItem
from app.main import app
from apps.desktop.app.core.config import DesktopShellSettings
from apps.desktop.app.shell.app import DesktopShellApp
from apps.telegram.app.bot.handlers import TelegramMessageHandler


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
        f"sqlite:///{tmp_path / 'step9-ch1-system-wide.db'}",
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


class _TestClientTelegramApi:
    def __init__(self, api_client: TestClient) -> None:
        self._api_client = api_client
        self.calls: list[tuple[str, str]] = []

    def submit_capture(
        self,
        *,
        raw_text: str,
        source_type: str = "telegram",
        source_ref: str | None = None,
    ) -> dict:
        self.calls.append(("capture", "/api/capture"))
        payload: dict[str, object] = {
            "raw_text": raw_text,
            "source_type": source_type,
        }
        if source_ref:
            payload["source_ref"] = source_ref
        response = self._api_client.post("/api/capture", json=payload)
        assert response.status_code == 201
        return response.json()["data"]

    def get_pending_list(self, *, limit: int = 5, offset: int = 0, status: str = "open") -> dict:
        self.calls.append(("pending_list", "/api/pending"))
        page = 1 if offset <= 0 else (offset // max(limit, 1)) + 1
        response = self._api_client.get(
            "/api/pending",
            params={"page": page, "page_size": limit, "status": status},
        )
        assert response.status_code == 200
        return response.json()["data"]

    def get_pending_detail(self, pending_id: int) -> dict:
        self.calls.append(("pending_detail", f"/api/pending/{pending_id}"))
        response = self._api_client.get(f"/api/pending/{pending_id}")
        assert response.status_code == 200
        return response.json()["data"]

    def confirm_pending(self, pending_id: int) -> dict:
        self.calls.append(("pending_confirm", f"/api/pending/{pending_id}/confirm"))
        response = self._api_client.post(f"/api/pending/{pending_id}/confirm", json={})
        assert response.status_code == 200
        return response.json()["data"]

    def discard_pending(self, pending_id: int) -> dict:
        self.calls.append(("pending_discard", f"/api/pending/{pending_id}/discard"))
        response = self._api_client.post(f"/api/pending/{pending_id}/discard", json={})
        assert response.status_code == 200
        return response.json()["data"]

    def fix_pending(self, pending_id: int, correction_text: str) -> dict:
        self.calls.append(("pending_fix", f"/api/pending/{pending_id}/fix"))
        response = self._api_client.post(
            f"/api/pending/{pending_id}/fix",
            json={"correction_text": correction_text},
        )
        assert response.status_code == 200
        return response.json()["data"]


class _TestClientDesktopStatusClient:
    def __init__(self, api_client: TestClient) -> None:
        self._api_client = api_client
        self.calls: list[str] = []

    def get_status(self) -> dict:
        self.calls.append("/api/healthz")
        response = self._api_client.get("/api/healthz")
        assert response.status_code == 200
        return response.json()["data"]

    def get_workbench_home(self) -> dict:
        self.calls.append("/api/workbench/home")
        response = self._api_client.get("/api/workbench/home")
        assert response.status_code == 200
        return response.json()["data"]

    def close(self) -> None:
        return None


def _telegram_update(text: str) -> dict:
    return {
        "message": {
            "message_id": 9,
            "chat": {"id": 10, "type": "private"},
            "from": {"id": 11},
            "text": text,
        }
    }


def _create_capture(db: Session, *, raw_text: str) -> int:
    capture = capture_repository.create_capture(
        db,
        source_type="test",
        source_ref="step9",
        raw_text=raw_text,
        status=CaptureStatus.COMMITTED,
    )
    db.flush()
    return capture.id


def _desktop_settings() -> DesktopShellSettings:
    return DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )


def test_chain_a_web_mainline_flow_closes_through_pending_confirm_formal_and_dashboard(
    api_client: TestClient,
    db: Session,
) -> None:
    capture_response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "web"},
    )
    assert capture_response.status_code == 201
    capture_data = capture_response.json()["data"]
    pending_id = capture_data["pending_item_id"]
    capture_id = capture_data["capture_id"]

    pending_detail_response = api_client.get(f"/api/pending/{pending_id}")
    assert pending_detail_response.status_code == 200
    pending_detail = pending_detail_response.json()["data"]
    assert pending_detail["status"] == "open"
    assert pending_detail["source_capture_id"] == capture_id

    fix_response = api_client.post(
        f"/api/pending/{pending_id}/fix",
        json={"correction_text": "今天花了25元午饭"},
    )
    assert fix_response.status_code == 200
    assert fix_response.json()["data"]["status"] == "open"

    confirm_response = api_client.post(
        f"/api/pending/{pending_id}/confirm",
        json={"note": "step9 chain a"},
    )
    assert confirm_response.status_code == 200
    assert confirm_response.json()["data"]["status"] == "confirmed"

    expense = db.query(ExpenseRecord).filter(ExpenseRecord.source_pending_id == pending_id).one()

    expense_detail_response = api_client.get(f"/api/expense/{expense.id}")
    assert expense_detail_response.status_code == 200
    expense_detail = expense_detail_response.json()["data"]
    assert expense_detail["source_capture_id"] == capture_id
    assert expense_detail["source_pending_id"] == pending_id

    dashboard_response = api_client.get("/api/dashboard")
    assert dashboard_response.status_code == 200
    dashboard = dashboard_response.json()["data"]
    assert dashboard["pending_summary"]["open_count"] == 0
    assert dashboard["expense_summary"]["created_in_current_month"] == 1
    assert isinstance(dashboard["recent_activity"], list)
    assert len(dashboard["recent_activity"]) >= 1


def test_chain_b_telegram_capture_stays_on_shared_capture_and_pending_semantics(
    api_client: TestClient,
) -> None:
    telegram_api = _TestClientTelegramApi(api_client)
    handler = TelegramMessageHandler(tracefold_api=telegram_api)

    capture_result = handler.handle_update(_telegram_update("买了咖啡"))
    assert capture_result is not None
    assert "Recorded. Pending item:" in capture_result.text

    pending_list_result = handler.handle_update(_telegram_update("/pending"))
    assert pending_list_result is not None
    assert "Open pending items:" in pending_list_result.text

    pending_list_response = api_client.get("/api/pending", params={"status": "open"})
    assert pending_list_response.status_code == 200
    pending_items = pending_list_response.json()["data"]["items"]
    assert len(pending_items) == 1

    workbench_home_response = api_client.get("/api/workbench/home")
    assert workbench_home_response.status_code == 200
    workbench_home = workbench_home_response.json()["data"]
    assert workbench_home["dashboard_summary"]["pending_summary"]["open_count"] == 1

    unsupported = handler.handle_update(_telegram_update("/force_insert 1"))
    assert unsupported is not None
    assert unsupported.text == "This command is not available."
    assert [call[0] for call in telegram_api.calls] == ["capture", "pending_list"]


def test_chain_c_formal_facts_rules_and_ai_derivations_remain_separate_and_visible(
    api_client: TestClient,
    db: Session,
) -> None:
    health_record = create_health_record(
        db,
        source_capture_id=_create_capture(db, raw_text="health capture"),
        source_pending_id=None,
        payload={"metric_type": "heart_rate", "value_text": "135"},
    )
    knowledge_entry = create_knowledge_entry(
        db,
        source_capture_id=_create_capture(db, raw_text="knowledge capture"),
        source_pending_id=None,
        payload={
            "title": "Morning routine note",
            "content": "I feel more focused when I prepare tomorrow before sleeping.",
            "source_text": "Manual note.",
        },
    )
    db.commit()

    health_detail_response = api_client.get(f"/api/health/{health_record.id}")
    assert health_detail_response.status_code == 200
    health_detail = health_detail_response.json()["data"]
    assert health_detail["metric_type"] == "heart_rate"
    assert "content_json" not in health_detail
    assert "severity" not in health_detail

    alerts_response = api_client.get(
        "/api/alerts",
        params={"source_domain": "health", "source_record_id": health_record.id},
    )
    assert alerts_response.status_code == 200
    alert_items = alerts_response.json()["data"]["items"]
    assert len(alert_items) == 1
    assert alert_items[0]["severity"] == "high"
    assert alert_items[0]["status"] == "open"

    knowledge_detail_response = api_client.get(f"/api/knowledge/{knowledge_entry.id}")
    assert knowledge_detail_response.status_code == 200
    knowledge_detail = knowledge_detail_response.json()["data"]
    assert knowledge_detail["title"] == "Morning routine note"
    assert "content_json" not in knowledge_detail

    derivation_response = api_client.get(
        "/api/ai-derivations/knowledge/{knowledge_id}".format(knowledge_id=knowledge_entry.id)
    )
    assert derivation_response.status_code == 200
    derivation = derivation_response.json()["data"]
    assert derivation["derivation_type"] == "knowledge_summary"
    assert derivation["status"] == "ready"

    dashboard_response = api_client.get("/api/dashboard")
    assert dashboard_response.status_code == 200
    dashboard = dashboard_response.json()["data"]
    assert dashboard["alert_summary"]["open_count"] >= 1
    assert dashboard["health_summary"]["created_in_last_7_days"] >= 1
    assert dashboard["knowledge_summary"]["created_in_last_7_days"] >= 1


def test_chain_d_desktop_shell_stays_shell_only_while_using_shared_workbench_and_recent_context(
    api_client: TestClient,
) -> None:
    capture_response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "web"},
    )
    pending_id = capture_response.json()["data"]["pending_item_id"]
    assert pending_id is not None

    api_client.get(f"/api/pending/{pending_id}")

    templates_response = api_client.get("/api/workbench/templates")
    templates = templates_response.json()["data"]["items"]
    pending_review_template = next(item for item in templates if item["name"] == "Pending Review")

    apply_response = api_client.post(
        f"/api/workbench/templates/{pending_review_template['template_id']}/apply",
        json={"set_as_default": False},
    )
    assert apply_response.status_code == 200

    desktop_client = _TestClientDesktopStatusClient(api_client)
    shell_app = DesktopShellApp(
        settings=_desktop_settings(),
        status_client=desktop_client,
    )

    bootstrap = shell_app.bootstrap()
    opened = shell_app.open_workbench()
    recent_response = api_client.get("/api/workbench/recent")

    assert bootstrap["service_status"] == "ok"
    assert opened["url"] == "http://localhost:3000/workbench"
    assert shell_app.state.active_mode_name == "Pending Review"
    assert shell_app.state.workbench_status_label == "Current mode: Pending Review"
    assert recent_response.status_code == 200
    recent_items = recent_response.json()["data"]["items"]
    assert len(recent_items) == 1
    assert recent_items[0]["route_snapshot"] == f"/pending/{pending_id}"
    assert desktop_client.calls == ["/api/healthz", "/api/workbench/home"]
    assert not hasattr(shell_app, "confirm_pending")
    assert not hasattr(shell_app, "create_template")


def test_chain_e_template_apply_changes_entry_context_without_mutating_formal_facts(
    api_client: TestClient,
    db: Session,
) -> None:
    before_counts = {
        "expense": db.query(ExpenseRecord).count(),
        "knowledge": db.query(KnowledgeEntry).count(),
        "health": db.query(HealthRecord).count(),
        "pending": db.query(PendingItem).count(),
    }

    shortcut_response = api_client.post(
        "/api/workbench/shortcuts",
        json={
            "label": "Open pending review",
            "target_type": "module_view",
            "target_payload_json": {
                "module": "pending",
                "view_key": "list",
                "query": {"status": "open", "sort_by": "created_at", "sort_order": "desc"},
            },
            "sort_order": 10,
            "is_enabled": True,
        },
    )
    assert shortcut_response.status_code == 201
    shortcut_id = shortcut_response.json()["data"]["shortcut_id"]

    template_response = api_client.post(
        "/api/workbench/templates",
        json={
            "template_type": "user",
            "name": "Pending Focus",
            "default_module": "pending",
            "default_view_key": "list",
            "default_query_json": {"status": "open", "sort_by": "created_at", "sort_order": "desc"},
            "description": "Focus on open pending work.",
            "scoped_shortcut_ids": [shortcut_id],
            "sort_order": 40,
            "is_enabled": True,
        },
    )
    assert template_response.status_code == 201
    template_id = template_response.json()["data"]["template_id"]

    apply_response = api_client.post(
        f"/api/workbench/templates/{template_id}/apply",
        json={"set_as_default": True},
    )
    assert apply_response.status_code == 200
    apply_data = apply_response.json()["data"]
    assert apply_data["template_applied"] is True
    assert apply_data["template_id"] == template_id

    home_response = api_client.get("/api/workbench/home")
    assert home_response.status_code == 200
    home = home_response.json()["data"]
    assert home["current_mode"]["template_id"] == template_id
    assert home["current_mode"]["default_module"] == "pending"
    assert len(home["pinned_shortcuts"]) == 1
    assert home["pinned_shortcuts"][0]["shortcut_id"] == shortcut_id
    assert "pending_summary" in home["dashboard_summary"]
    assert isinstance(home["recent_contexts"], list)

    after_counts = {
        "expense": db.query(ExpenseRecord).count(),
        "knowledge": db.query(KnowledgeEntry).count(),
        "health": db.query(HealthRecord).count(),
        "pending": db.query(PendingItem).count(),
    }
    assert after_counts == before_counts
