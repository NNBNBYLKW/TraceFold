from __future__ import annotations

from pathlib import Path

from sqlalchemy import create_engine, inspect

from app.db import init_db as init_db_module


def test_step8_workbench_schema_is_created_by_init_db(monkeypatch, tmp_path: Path) -> None:
    engine = create_engine(
        f"sqlite:///{tmp_path / 'workbench-migration.db'}",
        future=True,
        connect_args={"check_same_thread": False},
    )
    monkeypatch.setattr(init_db_module, "engine", engine)

    init_db_module.init_db()

    inspector = inspect(engine)
    table_names = set(inspector.get_table_names())

    assert "workbench_templates" in table_names
    assert "workbench_shortcuts" in table_names
    assert "workbench_recent_contexts" in table_names
    assert "workbench_preferences" in table_names

    engine.dispose()
