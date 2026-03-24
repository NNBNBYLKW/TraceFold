from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from app.db.base import Base
from app.db.session import get_db
from app.main import app


@pytest.fixture
def testing_session_local(tmp_path: Path):
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
        f"sqlite:///{tmp_path / 'task-api.db'}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    testing_session = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )
    Base.metadata.create_all(bind=engine)
    try:
        yield testing_session
    finally:
        Base.metadata.drop_all(bind=engine)
        engine.dispose()


@pytest.fixture
def db(testing_session_local) -> Generator[Session, None, None]:
    session = testing_session_local()
    try:
        yield session
    finally:
        session.close()


@pytest.fixture
def api_client(
    db: Session,
    testing_session_local,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    def override_get_db() -> Generator[Session, None, None]:
        yield db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    monkeypatch.setattr("app.domains.system_tasks.service.SessionLocal", testing_session_local)
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_post_task_run_creates_and_completes_dashboard_refresh_task(
    api_client: TestClient,
) -> None:
    response = api_client.post(
        "/api/tasks/dashboard_summary_refresh",
        json={"payload_json": {"requested_by": "api-test"}},
    )

    assert response.status_code == 201
    created = response.json()["data"]
    assert created["task_type"] == "dashboard_summary_refresh"
    assert created["status"] == "pending"

    detail_response = api_client.get(f"/api/tasks/{created['task_id']}")

    assert detail_response.status_code == 200
    detail = detail_response.json()["data"]
    assert detail["status"] == "succeeded"
    assert detail["attempt_count"] == 1
    assert detail["result_json"]["recent_activity_count"] >= 0


def test_get_tasks_lists_created_task_runs(api_client: TestClient) -> None:
    create_response = api_client.post(
        "/api/tasks/dashboard_summary_refresh",
        json={"payload_json": None},
    )
    task_id = create_response.json()["data"]["task_id"]

    response = api_client.get("/api/tasks?limit=10")

    assert response.status_code == 200
    body = response.json()["data"]
    assert body["total"] >= 1
    assert any(item["id"] == task_id for item in body["items"])


def test_get_tasks_supports_status_and_task_type_filters(api_client: TestClient) -> None:
    create_response = api_client.post(
        "/api/tasks/dashboard_summary_refresh",
        json={"payload_json": None},
    )
    task_id = create_response.json()["data"]["task_id"]

    response = api_client.get(
        "/api/tasks?status=succeeded&task_type=dashboard_summary_refresh&limit=10"
    )

    assert response.status_code == 200
    body = response.json()["data"]
    assert body["total"] >= 1
    assert any(item["id"] == task_id for item in body["items"])


def test_invalid_task_type_returns_uniform_error(api_client: TestClient) -> None:
    response = api_client.post(
        "/api/tasks/unsupported_task",
        json={"payload_json": None},
    )

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "INVALID_TASK_TYPE"


def test_task_failure_is_visible_via_detail(
    api_client: TestClient,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "app.domains.system_tasks.service.get_task_executor",
        lambda task_type: (lambda db, payload_json, task_id=None: (_ for _ in ()).throw(RuntimeError("task boom"))),
    )

    create_response = api_client.post(
        "/api/tasks/dashboard_summary_refresh",
        json={"payload_json": None},
    )
    task_id = create_response.json()["data"]["task_id"]

    detail_response = api_client.get(f"/api/tasks/{task_id}")

    assert detail_response.status_code == 200
    detail = detail_response.json()["data"]
    assert detail["status"] == "failed"
    assert detail["error_message"] == "task boom"
