from pathlib import Path

import pytest
from sqlalchemy import text

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import create_engine_and_session


def test_rejects_nested_runtime_directories(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="must not overlap"):
        BootstrapSettings(
            inbox_dir=tmp_path / "inbox",
            library_dir=tmp_path / "inbox" / "library",
            work_dir=tmp_path / "work",
            database_path=tmp_path / "media.sqlite3",
        )


def test_rejects_database_inside_inbox(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="database_path"):
        BootstrapSettings(
            inbox_dir=tmp_path / "inbox",
            library_dir=tmp_path / "library",
            work_dir=tmp_path / "work",
            database_path=tmp_path / "inbox" / "media.sqlite3",
        )


def test_database_sessions_keep_objects_live_and_enable_foreign_keys(tmp_path: Path) -> None:
    settings = BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
    )

    session_factory = create_engine_and_session(settings)

    assert session_factory.kw["expire_on_commit"] is False
    with session_factory() as session:
        assert session.execute(text("PRAGMA foreign_keys")).scalar_one() == 1

    session_factory.kw["bind"].dispose()


def test_web_distribution_is_disabled_by_default(tmp_path: Path) -> None:
    settings = BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
    )

    assert settings.web_dist_dir is None


def test_web_distribution_path_is_resolved(tmp_path: Path) -> None:
    web = tmp_path / "web"
    settings = BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
        web_dist_dir=web,
    )

    assert settings.web_dist_dir == web.resolve()
