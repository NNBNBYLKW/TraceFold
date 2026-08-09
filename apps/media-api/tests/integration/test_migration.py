from collections.abc import Generator
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from sqlalchemy import Engine, create_engine, inspect, text


MEDIA_TABLES = {
    "app_settings",
    "authors",
    "inbox_item_authors",
    "inbox_item_tags",
    "inbox_items",
    "inbox_status_events",
    "ingest_operations",
    "media_asset_authors",
    "media_asset_tags",
    "media_assets",
    "processing_jobs",
    "processing_plans",
    "processor_settings",
    "scan_candidates",
    "series",
    "source_files",
    "tags",
}


@pytest.fixture
def migrated_engine(tmp_path: Path) -> Generator[Engine, None, None]:
    api_root = Path(__file__).resolve().parents[2]
    database_url = f"sqlite+pysqlite:///{(tmp_path / 'migration.sqlite3').as_posix()}"
    config = Config(str(api_root / "alembic.ini"))
    config.set_main_option("script_location", str(api_root / "migrations"))
    config.set_main_option("sqlalchemy.url", database_url)

    command.upgrade(config, "head")
    engine = create_engine(database_url, future=True)
    try:
        yield engine
    finally:
        engine.dispose()


def test_initial_migration_creates_only_media_tables(migrated_engine: Engine) -> None:
    inspector = inspect(migrated_engine)
    tables = set(inspector.get_table_names())

    assert tables == MEDIA_TABLES | {"alembic_version"}
    assert "captures" not in tables
    assert "pending_items" not in tables

    expected_unique_constraints = {
        "scan_candidates": "uq_scan_candidate_path",
        "inbox_item_authors": "uq_inbox_author",
        "inbox_item_tags": "uq_inbox_tag",
        "media_asset_authors": "uq_asset_author",
        "media_asset_tags": "uq_asset_tag",
        "processing_jobs": "uq_processing_job_idempotency",
        "ingest_operations": "uq_ingest_operation_idempotency",
    }
    for table_name, constraint_name in expected_unique_constraints.items():
        names = {
            constraint["name"]
            for constraint in inspector.get_unique_constraints(table_name)
        }
        assert constraint_name in names

    with migrated_engine.connect() as connection:
        revision = connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one()
    assert revision == "20260809_0001"
