from __future__ import annotations

import os
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class E2EPaths:
    root: Path
    inbox: Path
    library: Path
    work: Path
    database: Path


def resolve_e2e_paths(web_dir: Path) -> E2EPaths:
    root = (web_dir / ".e2e-data").resolve()
    return E2EPaths(
        root=root,
        inbox=(root / "inbox").resolve(),
        library=(root / "library").resolve(),
        work=(root / "work").resolve(),
        database=(root / "media.sqlite3").resolve(),
    )


def _assert_safe_paths(paths: E2EPaths) -> None:
    root = paths.root.resolve()
    expected_suffix = ("apps", "media-web", ".e2e-data")
    if tuple(part.casefold() for part in root.parts[-3:]) != expected_suffix:
        raise ValueError("E2E root must end with apps/media-web/.e2e-data")
    expected = {
        "inbox": root / "inbox",
        "library": root / "library",
        "work": root / "work",
        "database": root / "media.sqlite3",
    }
    actual = {
        "inbox": paths.inbox,
        "library": paths.library,
        "work": paths.work,
        "database": paths.database,
    }
    for name, target in actual.items():
        if target != expected[name] or target.parent != root:
            raise ValueError(f"unsafe E2E child path: {name}")
        if (target.exists() or target.is_symlink()) and target.resolve().parent != root:
            raise ValueError(f"E2E child escapes dedicated root: {name}")


def reset_e2e_data(paths: E2EPaths) -> None:
    _assert_safe_paths(paths)
    paths.root.mkdir(parents=True, exist_ok=True)
    for directory in (paths.inbox, paths.library, paths.work):
        if directory.is_dir() and not directory.is_symlink():
            shutil.rmtree(directory)
        elif directory.exists() or directory.is_symlink():
            directory.unlink()
    if paths.database.exists() or paths.database.is_symlink():
        paths.database.unlink()


def _configure_environment(paths: E2EPaths) -> None:
    os.environ["TRACEFOLD_MEDIA_INBOX_DIR"] = str(paths.inbox)
    os.environ["TRACEFOLD_MEDIA_LIBRARY_DIR"] = str(paths.library)
    os.environ["TRACEFOLD_MEDIA_WORK_DIR"] = str(paths.work)
    os.environ["TRACEFOLD_MEDIA_DATABASE_PATH"] = str(paths.database)
    os.environ["TRACEFOLD_MEDIA_SCAN_INTERVAL_SECONDS"] = "2"


def _migrate(api_dir: Path, database: Path) -> None:
    from alembic import command
    from alembic.config import Config

    config = Config(str(api_dir / "alembic.ini"))
    config.set_main_option(
        "sqlalchemy.url",
        f"sqlite+pysqlite:///{database.as_posix()}",
    )
    command.upgrade(config, "head")


def _enable_automatic_scan(database: Path) -> None:
    from sqlalchemy import create_engine
    from sqlalchemy.orm import Session

    from media_api.settings.models import AppSetting

    engine = create_engine(
        f"sqlite+pysqlite:///{database.as_posix()}",
        future=True,
    )
    with Session(engine) as session:
        session.merge(AppSetting(key="automatic_scan_enabled", value_json=True))
        session.commit()
    engine.dispose()


def main() -> None:
    api_dir = Path(__file__).resolve().parents[1]
    web_dir = api_dir.parent / "media-web"
    paths = resolve_e2e_paths(web_dir)
    reset_e2e_data(paths)
    paths.inbox.mkdir()
    paths.library.mkdir()
    paths.work.mkdir()
    _configure_environment(paths)
    sys.path.insert(0, str(api_dir / "src"))
    _migrate(api_dir, paths.database)
    _enable_automatic_scan(paths.database)

    import uvicorn

    from media_api.main import create_app

    uvicorn.run(create_app(), host="127.0.0.1", port=8101, log_level="warning")


if __name__ == "__main__":
    main()
