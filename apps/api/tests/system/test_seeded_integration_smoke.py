from __future__ import annotations

from pathlib import Path

from sqlalchemy import create_engine, text

from scripts.smoke_seeded_demo import main as smoke_seeded_demo_main


def _sqlite_url(db_path: Path) -> str:
    return f"sqlite:///{db_path}"


def test_smoke_seeded_demo_script_runs_against_fresh_demo_db(tmp_path: Path) -> None:
    db_path = tmp_path / "seeded-integration-smoke.db"
    database_url = _sqlite_url(db_path)

    exit_code = smoke_seeded_demo_main(
        [
            "--db-url",
            database_url,
            "--expenses",
            "12",
            "--knowledge",
            "8",
            "--health",
            "9",
        ]
    )

    assert exit_code == 0

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        with engine.connect() as connection:
            expense_count = connection.execute(text("SELECT COUNT(*) FROM expense_records")).scalar_one()
            knowledge_count = connection.execute(text("SELECT COUNT(*) FROM knowledge_entries")).scalar_one()
            health_count = connection.execute(text("SELECT COUNT(*) FROM health_records")).scalar_one()
            alert_count = connection.execute(text("SELECT COUNT(*) FROM rule_alerts")).scalar_one()
            task_count = connection.execute(text("SELECT COUNT(*) FROM task_runs")).scalar_one()
            derivation_count = connection.execute(
                text(
                    """
                    SELECT COUNT(*)
                    FROM ai_derivations
                    WHERE target_type = 'knowledge'
                      AND derivation_kind = 'knowledge_summary'
                    """
                )
            ).scalar_one()
    finally:
        engine.dispose()

    assert expense_count == 12
    assert knowledge_count == 8
    assert health_count == 9
    assert alert_count >= 1
    assert task_count >= 1
    assert derivation_count >= 1
