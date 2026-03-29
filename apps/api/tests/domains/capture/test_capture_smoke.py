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
from app.domains.pending import repository as pending_repository
from app.domains.pending.models import PendingStatus
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

    engine = create_engine(
        f"sqlite:///{tmp_path / 'capture-http.db'}",
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


def test_post_capture_routes_medium_confidence_input_to_pending(
    api_client: TestClient,
    db: Session,
) -> None:
    response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "manual"},
    )

    assert response.status_code == 201
    payload = response.json()
    assert payload["success"] is True
    assert payload["message"] == "Capture submitted."

    data = payload["data"]
    assert data["capture_created"] is True
    assert data["route"] == "pending"
    assert data["status"] == CaptureStatus.PENDING
    assert data["target_domain"] == "expense"
    assert data["pending_item_id"] is not None
    assert data["formal_record_id"] is None

    capture = capture_repository.get_capture_by_id(db, data["capture_id"])
    pending_item = pending_repository.get_pending_item_by_id(db, data["pending_item_id"])

    assert capture is not None
    assert capture.status == CaptureStatus.PENDING
    assert capture.raw_text == "买了咖啡"
    assert pending_item is not None
    assert pending_item.status == PendingStatus.OPEN
    assert pending_item.capture_id == capture.id


def test_post_capture_routes_high_confidence_input_to_formal_commit(
    api_client: TestClient,
    db: Session,
) -> None:
    response = api_client.post(
        "/api/capture",
        json={"raw_text": "今天花了25元午饭", "source_type": "manual"},
    )

    assert response.status_code == 201
    data = response.json()["data"]
    assert data["capture_created"] is True
    assert data["route"] == "committed"
    assert data["status"] == CaptureStatus.COMMITTED
    assert data["target_domain"] == "expense"
    assert data["pending_item_id"] is None
    assert data["formal_record_id"] is not None

    capture = capture_repository.get_capture_by_id(db, data["capture_id"])
    expense_record = db.get(ExpenseRecord, data["formal_record_id"])

    assert capture is not None
    assert capture.status == CaptureStatus.COMMITTED
    assert capture.finalized_at is not None
    assert expense_record is not None
    assert expense_record.source_capture_id == capture.id
    assert expense_record.source_pending_id is None
    assert expense_record.amount == "25"
    assert expense_record.currency == "CNY"


def test_post_capture_rejects_blank_raw_text(api_client: TestClient) -> None:
    response = api_client.post(
        "/api/capture",
        json={"raw_text": "   ", "source_type": "manual"},
    )

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["error"]["code"] == "INVALID_CAPTURE_RAW_TEXT"


def test_capture_pending_flow_can_be_read_and_confirmed_via_uniform_http_path(
    api_client: TestClient,
    db: Session,
) -> None:
    submit_response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "manual"},
    )

    assert submit_response.status_code == 201
    submit_data = submit_response.json()["data"]
    pending_id = submit_data["pending_item_id"]
    assert submit_data["route"] == "pending"
    assert pending_id is not None

    detail_response = api_client.get(f"/api/pending/{pending_id}")

    assert detail_response.status_code == 200
    detail_data = detail_response.json()["data"]
    assert detail_data["id"] == pending_id
    assert detail_data["status"] == PendingStatus.OPEN
    assert detail_data["source_capture_id"] == submit_data["capture_id"]

    confirm_response = api_client.post(
        f"/api/pending/{pending_id}/fix",
        json={"correction_text": "今天花了25元午饭"},
    )
    assert confirm_response.status_code == 200
    assert confirm_response.json()["data"]["status"] == PendingStatus.OPEN

    confirm_response = api_client.post(
        f"/api/pending/{pending_id}/confirm",
        json={"note": "confirmed from smoke flow"},
    )

    assert confirm_response.status_code == 200
    confirm_data = confirm_response.json()["data"]
    assert confirm_data["pending_id"] == pending_id
    assert confirm_data["status"] == PendingStatus.CONFIRMED

    capture = capture_repository.get_capture_by_id(db, submit_data["capture_id"])

    assert capture is not None
    assert capture.status == CaptureStatus.COMMITTED
    assert capture.finalized_at is not None


def test_capture_list_shows_upstream_status_and_stage_visibility(
    api_client: TestClient,
) -> None:
    pending_response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "manual", "source_ref": "wechat"},
    )
    committed_response = api_client.post(
        "/api/capture",
        json={"raw_text": "今天花了25元午饭", "source_type": "manual"},
    )

    pending_capture_id = pending_response.json()["data"]["capture_id"]
    committed_capture_id = committed_response.json()["data"]["capture_id"]

    response = api_client.get("/api/capture")

    assert response.status_code == 200
    payload = response.json()
    assert payload["success"] is True
    assert payload["message"] == "Capture items fetched."

    items = payload["data"]["items"]
    pending_item = next(item for item in items if item["id"] == pending_capture_id)
    committed_item = next(item for item in items if item["id"] == committed_capture_id)

    assert pending_item["status"] == CaptureStatus.PENDING
    assert pending_item["current_stage"] == "pending_review"
    assert pending_item["target_domain"] == "expense"
    assert pending_item["source_ref"] == "wechat"
    assert pending_item["pending_item_id"] is not None
    assert pending_item["formal_record_id"] is None

    assert committed_item["status"] == CaptureStatus.COMMITTED
    assert committed_item["current_stage"] == "formal_record"
    assert committed_item["target_domain"] == "expense"
    assert committed_item["pending_item_id"] is None
    assert committed_item["formal_record_id"] is not None


def test_capture_detail_shows_parse_pending_and_formal_linkage_after_pending_resolution(
    api_client: TestClient,
) -> None:
    submit_response = api_client.post(
        "/api/capture",
        json={"raw_text": "买了咖啡", "source_type": "manual"},
    )
    submit_data = submit_response.json()["data"]
    capture_id = submit_data["capture_id"]
    pending_id = submit_data["pending_item_id"]

    assert pending_id is not None

    fix_response = api_client.post(
        f"/api/pending/{pending_id}/fix",
        json={"correction_text": "今天花了25元午饭"},
    )
    assert fix_response.status_code == 200

    confirm_response = api_client.post(
        f"/api/pending/{pending_id}/confirm",
        json={"note": "confirm from capture detail test"},
    )
    assert confirm_response.status_code == 200

    detail_response = api_client.get(f"/api/capture/{capture_id}")

    assert detail_response.status_code == 200
    detail = detail_response.json()["data"]

    assert detail["id"] == capture_id
    assert detail["status"] == CaptureStatus.COMMITTED
    assert detail["current_stage"] == "formal_record"
    assert detail["parse_result"] is not None
    assert detail["parse_result"]["capture_id"] == capture_id
    assert detail["pending_item"] is not None
    assert detail["pending_item"]["id"] == pending_id
    assert detail["pending_item"]["status"] == PendingStatus.CONFIRMED
    assert detail["pending_item"]["actionable"] is False
    assert detail["formal_result"] is not None
    assert detail["formal_result"]["target_domain"] == "expense"
    assert detail["formal_result"]["source_pending_id"] == pending_id
    assert detail["formal_result"]["record_id"] is not None
