from __future__ import annotations

from pathlib import Path

from sqlalchemy import create_engine, inspect

from app.db.init_db import init_db
from app.db.migrations import get_current_revision, upgrade_database


EXPECTED_TABLES = {
    "ai_derivations",
    "alembic_version",
    "capture_records",
    "expense_records",
    "health_records",
    "knowledge_entries",
    "parse_results",
    "pending_items",
    "pending_review_actions",
    "rule_alerts",
    "task_runs",
    "workbench_preferences",
    "workbench_recent_contexts",
    "workbench_shortcuts",
    "workbench_templates",
}


def _sqlite_url(db_path: Path) -> str:
    return f"sqlite:///{db_path}"


def test_upgrade_database_creates_phase1_schema(tmp_path: Path) -> None:
    db_path = tmp_path / "migration-head.db"
    database_url = _sqlite_url(db_path)

    upgrade_database("head", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        assert EXPECTED_TABLES.issubset(set(inspector.get_table_names()))
    finally:
        engine.dispose()
    assert get_current_revision(database_url=database_url) == "20260323_0005"


def test_init_db_uses_formal_migrations(tmp_path: Path) -> None:
    db_path = tmp_path / "init-db-upgrade.db"
    database_url = _sqlite_url(db_path)

    init_db(database_url=database_url)
    init_db(database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        assert EXPECTED_TABLES.issubset(set(inspector.get_table_names()))
    finally:
        engine.dispose()
    assert get_current_revision(database_url=database_url) == "20260323_0005"


def test_upgrade_from_phase1_baseline_migrates_background_tasks_to_task_runs(tmp_path: Path) -> None:
    db_path = tmp_path / "task-runtime-upgrade.db"
    database_url = _sqlite_url(db_path)

    upgrade_database("20260323_0001", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        assert "background_tasks" in set(inspector.get_table_names())
    finally:
        engine.dispose()

    upgrade_database("head", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        table_names = set(inspector.get_table_names())
        task_run_columns = {column["name"] for column in inspector.get_columns("task_runs")}
    finally:
        engine.dispose()

    assert "background_tasks" not in table_names
    assert "task_runs" in table_names
    assert {
        "task_type",
        "status",
        "trigger_source",
        "attempt_count",
        "requested_at",
        "updated_at",
    }.issubset(task_run_columns)
    assert get_current_revision(database_url=database_url) == "20260323_0005"


def test_upgrade_from_task_runtime_head_migrates_alert_results_to_rule_alerts(tmp_path: Path) -> None:
    db_path = tmp_path / "rule-alert-upgrade.db"
    database_url = _sqlite_url(db_path)

    upgrade_database("20260323_0003", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        with engine.begin() as connection:
            connection.exec_driver_sql(
                """
                INSERT INTO alert_results (
                    source_domain,
                    source_record_id,
                    rule_code,
                    severity,
                    status,
                    title,
                    message,
                    explanation,
                    triggered_at
                ) VALUES (
                    'health',
                    7,
                    'HEALTH_HEART_RATE_HIGH_V1',
                    'high',
                    'viewed',
                    'High heart rate',
                    'Needs attention.',
                    'Keep monitoring.',
                    '2026-03-23 12:00:00'
                )
                """
            )
    finally:
        engine.dispose()

    upgrade_database("head", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        table_names = set(inspector.get_table_names())
        rule_alert_columns = {column["name"] for column in inspector.get_columns("rule_alerts")}
        with engine.connect() as connection:
            row = connection.exec_driver_sql(
                """
                SELECT domain, rule_key, status, source_record_type
                FROM rule_alerts
                WHERE source_record_id = 7
                """
            ).one()
    finally:
        engine.dispose()

    assert "alert_results" not in table_names
    assert "rule_alerts" in table_names
    assert {
        "domain",
        "rule_key",
        "status",
        "source_record_type",
        "resolution_note",
    }.issubset(rule_alert_columns)
    assert row == ("health", "HEALTH_HEART_RATE_HIGH_V1", "acknowledged", "health_record")


def test_upgrade_from_rule_alert_head_migrates_ai_derivation_results_to_ai_derivations(tmp_path: Path) -> None:
    db_path = tmp_path / "ai-derivation-upgrade.db"
    database_url = _sqlite_url(db_path)

    upgrade_database("20260323_0004", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        with engine.begin() as connection:
            connection.exec_driver_sql(
                """
                INSERT INTO ai_derivation_results (
                    target_domain,
                    target_record_id,
                    derivation_type,
                    status,
                    model_name,
                    model_version,
                    generated_at,
                    content_json,
                    error_message
                ) VALUES (
                    'knowledge',
                    9,
                    'knowledge_summary',
                    'completed',
                    'tracefold-knowledge-summary',
                    'v1',
                    '2026-03-23 12:00:00',
                    json('{"summary":"Legacy summary","key_points":["A"],"keywords":["knowledge"]}'),
                    NULL
                )
                """
            )
    finally:
        engine.dispose()

    upgrade_database("head", database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        table_names = set(inspector.get_table_names())
        ai_derivation_columns = {column["name"] for column in inspector.get_columns("ai_derivations")}
        with engine.connect() as connection:
            row = connection.exec_driver_sql(
                """
                SELECT target_type, derivation_kind, status, model_key
                FROM ai_derivations
                WHERE target_id = 9
                """
            ).one()
    finally:
        engine.dispose()

    assert "ai_derivation_results" not in table_names
    assert "ai_derivations" in table_names
    assert {
        "target_type",
        "target_id",
        "derivation_kind",
        "status",
        "model_key",
        "source_basis_json",
        "invalidated_at",
        "updated_at",
    }.issubset(ai_derivation_columns)
    assert row == ("knowledge", "knowledge_summary", "ready", "tracefold-knowledge-summary")
    assert get_current_revision(database_url=database_url) == "20260323_0005"
