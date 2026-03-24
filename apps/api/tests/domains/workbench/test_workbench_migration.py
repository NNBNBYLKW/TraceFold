from __future__ import annotations

from pathlib import Path

from sqlalchemy import create_engine, inspect

from app.db import init_db as init_db_module


def test_step8_workbench_schema_is_created_by_init_db(tmp_path: Path) -> None:
    db_path = tmp_path / "workbench-migration.db"
    database_url = f"sqlite:///{db_path}"

    init_db_module.init_db(database_url=database_url)

    engine = create_engine(database_url, future=True, connect_args={"check_same_thread": False})
    try:
        inspector = inspect(engine)
        table_names = set(inspector.get_table_names())
    finally:
        engine.dispose()

    assert "workbench_templates" in table_names
    assert "workbench_shortcuts" in table_names
    assert "workbench_recent_contexts" in table_names
    assert "workbench_preferences" in table_names
