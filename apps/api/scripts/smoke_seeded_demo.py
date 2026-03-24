from __future__ import annotations

import argparse
import sys
from pathlib import Path

from fastapi.testclient import TestClient


API_ROOT = Path(__file__).resolve().parents[1]
if str(API_ROOT) not in sys.path:
    sys.path.insert(0, str(API_ROOT))

from app.db.migrations import get_current_revision, get_head_revision, upgrade_database
from app.db.session import build_session_local, get_db
from app.domains.knowledge.models import KnowledgeEntry
from app.main import app
from app.seed.demo_data import DemoSeedOptions, seed_demo_data


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run fresh demo DB integration smoke for TraceFold API.",
    )
    parser.add_argument("--db-url", dest="db_url", required=True)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--random-seed", type=int, default=20260323)
    parser.add_argument("--expenses", type=_non_negative_int, default=60)
    parser.add_argument("--knowledge", dest="knowledge_entries", type=_non_negative_int, default=30)
    parser.add_argument("--health", dest="health_records", type=_non_negative_int, default=45)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    upgrade_database("head", database_url=args.db_url)

    session_local = build_session_local(database_url=args.db_url)
    engine = session_local.kw.get("bind")
    db = session_local()
    try:
        seed_demo_data(
            db,
            options=DemoSeedOptions(
                expenses=args.expenses,
                knowledge_entries=args.knowledge_entries,
                health_records=args.health_records,
                random_seed=args.random_seed,
                force=args.force,
                with_derivations=True,
            ),
        )
        _configure_app_for_smoke(
            db=db,
            session_local=session_local,
            engine=engine,
            db_url=args.db_url,
        )
        with TestClient(app) as client:
            report = run_smoke_checks(client=client, db=db)
    finally:
        app.dependency_overrides.clear()
        db.close()
        if engine is not None:
            engine.dispose()

    print("TraceFold seeded integration smoke completed.")
    for line in report:
        print(line)
    return 0


def run_smoke_checks(*, client: TestClient, db) -> list[str]:
    report: list[str] = []

    healthz_response = client.get("/api/healthz")
    _require_ok(healthz_response.status_code == 200, "/api/healthz failed")
    report.append("/api/healthz OK")

    status_response = client.get("/api/system/status")
    _require_ok(status_response.status_code == 200, "/api/system/status failed")
    status_payload = status_response.json()["data"]
    _require_ok(status_payload["db_status"] == "ok", "runtime status db_status is not ok")
    _require_ok(status_payload["migration_status"] == "ok", "runtime status migration_status is not ok")
    report.append("/api/system/status OK")

    expense_response = client.get("/api/expense", params={"page": 1, "page_size": 20})
    _require_ok(expense_response.status_code == 200, "/api/expense failed")
    _require_ok(expense_response.json()["data"]["total"] > 0, "expense list is empty")
    report.append("/api/expense non-empty")

    knowledge_response = client.get("/api/knowledge", params={"page": 1, "page_size": 20})
    _require_ok(knowledge_response.status_code == 200, "/api/knowledge failed")
    _require_ok(knowledge_response.json()["data"]["total"] > 0, "knowledge list is empty")
    knowledge_id = db.query(KnowledgeEntry.id).order_by(KnowledgeEntry.id.asc()).first()[0]
    knowledge_detail_response = client.get(f"/api/knowledge/{knowledge_id}")
    _require_ok(knowledge_detail_response.status_code == 200, "knowledge detail failed")
    derivation_response = client.get(f"/api/ai-derivations/knowledge/{knowledge_id}")
    _require_ok(derivation_response.status_code == 200, "knowledge derivation read failed")
    report.append("/api/knowledge and /api/ai-derivations non-empty")

    health_response = client.get("/api/health", params={"page": 1, "page_size": 20})
    _require_ok(health_response.status_code == 200, "/api/health failed")
    _require_ok(health_response.json()["data"]["total"] > 0, "health list is empty")
    report.append("/api/health non-empty")

    alerts_response = client.get("/api/alerts", params={"domain": "health", "status": "open"})
    _require_ok(alerts_response.status_code == 200, "/api/alerts failed")
    _require_ok(alerts_response.json()["data"]["total"] >= 1, "no open health alerts found")
    report.append("/api/alerts contains open health alerts")

    tasks_response = client.get("/api/tasks", params={"limit": 20})
    _require_ok(tasks_response.status_code == 200, "/api/tasks failed")
    _require_ok(tasks_response.json()["data"]["total"] >= 1, "no task runs found")
    report.append("/api/tasks contains task runs")

    dashboard_response = client.get("/api/dashboard")
    _require_ok(dashboard_response.status_code == 200, "/api/dashboard failed")
    dashboard_payload = dashboard_response.json()["data"]
    _require_ok(
        dashboard_payload["expense_summary"]["created_in_current_month"] > 0
        or dashboard_payload["knowledge_summary"]["created_in_last_30_days"] > 0
        or dashboard_payload["health_summary"]["created_in_last_7_days"] > 0,
        "dashboard summary is still empty",
    )
    report.append("/api/dashboard non-empty")

    workbench_response = client.get("/api/workbench/home")
    _require_ok(workbench_response.status_code == 200, "/api/workbench/home failed")
    workbench_payload = workbench_response.json()["data"]
    _require_ok(len(workbench_payload["templates"]) >= 1, "workbench templates missing")
    _require_ok(
        workbench_payload["dashboard_summary"]["knowledge_summary"]["created_in_last_30_days"] > 0
        or workbench_payload["dashboard_summary"]["expense_summary"]["created_in_current_month"] > 0,
        "workbench dashboard summary is empty",
    )
    report.append("/api/workbench/home non-empty")

    return report


def _configure_app_for_smoke(*, db, session_local, engine, db_url: str) -> None:
    import app.main as app_main_module
    import app.core.runtime_status as runtime_status_module

    def override_get_db():
        yield db

    app_main_module.init_db = lambda: None
    runtime_status_module.engine = engine
    runtime_status_module.SessionLocal = session_local
    runtime_status_module.get_current_revision = lambda: get_current_revision(database_url=db_url)
    runtime_status_module.get_head_revision = lambda: get_head_revision(database_url=db_url)
    app.dependency_overrides[get_db] = override_get_db


def _require_ok(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def _non_negative_int(value: str) -> int:
    parsed = int(value)
    if parsed < 0:
        raise argparse.ArgumentTypeError("value must be greater than or equal to 0")
    return parsed


if __name__ == "__main__":
    raise SystemExit(main())
