from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

from fastapi.testclient import TestClient
import pytest
from sqlalchemy import create_engine, func, text
from sqlalchemy.orm import Session, sessionmaker

from app.db.database_url import resolve_database_url
from app.db.migrations import upgrade_database
from app.db.session import get_db
from app.domains.capture import service as capture_service
from app.domains.ai_derivations.models import AiDerivation
from app.domains.expense.models import ExpenseRecord
from app.domains.expense.service import create_expense_record
from app.domains.health.models import HealthRecord
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.system_tasks.models import TaskRun
from app.main import app
from app.seed.demo_data import DemoSeedOptions, seed_demo_data
from scripts.run_migrations import main as run_migrations_main
from scripts.seed_demo_data import main as seed_demo_data_main


def _sqlite_url(db_path: Path) -> str:
    return f"sqlite:///{db_path}"


@pytest.fixture
def migrated_db(tmp_path: Path) -> Generator[Session, None, None]:
    db_path = tmp_path / "seed-demo.db"
    database_url = _sqlite_url(db_path)
    upgrade_database("head", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    testing_session_local = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )
    session = testing_session_local()
    try:
        yield session
    finally:
        session.close()
        engine.dispose()


@pytest.fixture
def api_client(
    migrated_db: Session,
    monkeypatch: pytest.MonkeyPatch,
) -> Generator[TestClient, None, None]:
    engine = migrated_db.get_bind()
    runtime_session_local = sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )

    def override_get_db() -> Generator[Session, None, None]:
        yield migrated_db

    monkeypatch.setattr("app.main.init_db", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.engine", engine)
    monkeypatch.setattr("app.core.runtime_status.SessionLocal", runtime_session_local)
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()


def test_seed_demo_data_populates_formal_and_smoke_read_paths(
    migrated_db: Session,
    api_client: TestClient,
) -> None:
    result = seed_demo_data(
        migrated_db,
        options=DemoSeedOptions(
            expenses=18,
            knowledge_entries=10,
            health_records=12,
            random_seed=20260323,
            with_derivations=True,
        ),
    )

    assert result.expense_count == 18
    assert result.knowledge_count == 10
    assert result.health_count == 12
    assert result.health_alert_count >= 4
    assert result.knowledge_derivation_count == 10
    assert result.task_run_count >= 2

    assert migrated_db.query(func.count(ExpenseRecord.id)).scalar() == 18
    assert migrated_db.query(func.count(KnowledgeEntry.id)).scalar() == 10
    assert migrated_db.query(func.count(HealthRecord.id)).scalar() == 12
    assert migrated_db.query(func.count(TaskRun.id)).scalar() >= 2
    assert (
        migrated_db.query(func.count(AiDerivation.id))
        .filter(AiDerivation.target_type == "knowledge")
        .filter(AiDerivation.derivation_kind == "knowledge_summary")
        .scalar()
        == 10
    )

    expense_list_response = api_client.get("/api/expense", params={"page": 1, "page_size": 20})
    assert expense_list_response.status_code == 200
    assert expense_list_response.json()["data"]["total"] == 18

    knowledge_list_response = api_client.get("/api/knowledge", params={"page": 1, "page_size": 20})
    assert knowledge_list_response.status_code == 200
    assert knowledge_list_response.json()["data"]["total"] == 10

    knowledge_id = migrated_db.query(KnowledgeEntry.id).order_by(KnowledgeEntry.id.asc()).first()[0]
    knowledge_detail_response = api_client.get(f"/api/knowledge/{knowledge_id}")
    assert knowledge_detail_response.status_code == 200

    health_list_response = api_client.get("/api/health", params={"page": 1, "page_size": 20})
    assert health_list_response.status_code == 200
    assert health_list_response.json()["data"]["total"] == 12

    health_id = migrated_db.query(HealthRecord.id).order_by(HealthRecord.id.asc()).first()[0]
    health_detail_response = api_client.get(f"/api/health/{health_id}")
    assert health_detail_response.status_code == 200

    derivation_response = api_client.get(f"/api/ai-derivations/knowledge/{knowledge_id}")
    assert derivation_response.status_code == 200
    assert derivation_response.json()["data"]["status"] == "ready"

    alerts_response = api_client.get("/api/alerts", params={"domain": "health", "status": "open"})
    assert alerts_response.status_code == 200
    assert alerts_response.json()["data"]["total"] >= 1

    dashboard_response = api_client.get("/api/dashboard")
    assert dashboard_response.status_code == 200
    assert dashboard_response.json()["data"]["expense_summary"]["created_in_current_month"] >= 1

    status_response = api_client.get("/api/system/status")
    assert status_response.status_code == 200
    assert status_response.json()["data"]["task_runtime_status"] == "ready"


def test_seed_demo_data_refuses_second_run_without_force(migrated_db: Session) -> None:
    seed_demo_data(
        migrated_db,
        options=DemoSeedOptions(
            expenses=6,
            knowledge_entries=4,
            health_records=6,
        ),
    )

    with pytest.raises(RuntimeError, match="demo formal records already exist"):
        seed_demo_data(
            migrated_db,
            options=DemoSeedOptions(
                expenses=6,
                knowledge_entries=4,
                health_records=6,
            ),
        )


def test_seed_demo_data_force_reseeds_prior_demo_dataset(migrated_db: Session) -> None:
    first_result = seed_demo_data(
        migrated_db,
        options=DemoSeedOptions(
            expenses=7,
            knowledge_entries=5,
            health_records=6,
        ),
    )
    second_result = seed_demo_data(
        migrated_db,
        options=DemoSeedOptions(
            expenses=7,
            knowledge_entries=5,
            health_records=6,
            force=True,
            with_alerts=True,
            with_derivations=True,
        ),
    )

    assert first_result.expense_count == second_result.expense_count == 7
    assert first_result.knowledge_count == second_result.knowledge_count == 5
    assert first_result.health_count == second_result.health_count == 6
    assert migrated_db.query(func.count(ExpenseRecord.id)).scalar() == 7
    assert migrated_db.query(func.count(KnowledgeEntry.id)).scalar() == 5
    assert migrated_db.query(func.count(HealthRecord.id)).scalar() == 6


def test_seed_demo_data_refuses_force_over_non_demo_formal_data(migrated_db: Session) -> None:
    capture = capture_service.submit_capture(
        migrated_db,
        source_type="manual",
        source_ref="real-dev-data",
        raw_text="Manual expense entry",
        raw_payload_json={"amount": "88.00", "currency": "CNY"},
    )
    create_expense_record(
        migrated_db,
        source_capture_id=capture.id,
        source_pending_id=None,
        payload={
            "amount": "88.00",
            "currency": "CNY",
            "category": "groceries",
            "note": "Manual non-demo row.",
        },
    )
    migrated_db.commit()

    with pytest.raises(RuntimeError, match="non-demo formal records"):
        seed_demo_data(
            migrated_db,
            options=DemoSeedOptions(
                force=True,
            ),
        )


def test_seed_cli_main_seeds_migrated_database(tmp_path: Path) -> None:
    db_path = tmp_path / "seed-cli.db"
    database_url = _sqlite_url(db_path)
    upgrade_database("head", database_url=database_url)

    exit_code = seed_demo_data_main(
        [
            "--db-url",
            database_url,
            "--expenses",
            "5",
            "--knowledge",
            "4",
            "--health",
            "6",
            "--with-derivations",
        ]
    )

    assert exit_code == 0


def test_relative_sqlite_db_url_hits_same_database_for_migration_and_seed() -> None:
    relative_db_path = Path("data") / "pytest-seed-relative.db"
    relative_db_path.parent.mkdir(parents=True, exist_ok=True)
    if relative_db_path.exists():
        relative_db_path.unlink()

    relative_database_url = f"sqlite:///./{relative_db_path.as_posix()}"
    resolved_database_url = resolve_database_url(relative_database_url)

    try:
        migrate_exit_code = run_migrations_main(["upgrade", "head", "--db-url", relative_database_url])
        assert migrate_exit_code == 0

        seed_exit_code = seed_demo_data_main(
            [
                "--db-url",
                relative_database_url,
                "--expenses",
                "5",
                "--knowledge",
                "4",
                "--health",
                "6",
                "--with-derivations",
            ]
        )
        assert seed_exit_code == 0

        engine = create_engine(resolved_database_url, future=True, connect_args={"check_same_thread": False})
        try:
            with engine.connect() as connection:
                expense_count = connection.execute(text("SELECT COUNT(*) FROM expense_records")).scalar_one()
                knowledge_count = connection.execute(text("SELECT COUNT(*) FROM knowledge_entries")).scalar_one()
                health_count = connection.execute(text("SELECT COUNT(*) FROM health_records")).scalar_one()
        finally:
            engine.dispose()

        assert expense_count == 5
        assert knowledge_count == 4
        assert health_count == 6
    finally:
        if relative_db_path.exists():
            relative_db_path.unlink()
