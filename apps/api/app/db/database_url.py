from __future__ import annotations

from pathlib import Path


_SQLITE_URL_PREFIX = "sqlite:///"


def resolve_database_url(database_url: str) -> str:
    """
    Normalize SQLite file URLs so every caller hits the same physical DB file.

    Relative SQLite paths can drift between plain SQLAlchemy engine creation and
    Alembic-driven migration commands. Resolving relative file paths up front
    keeps migration, seed, and runtime reads aligned.
    """

    if not database_url.startswith(_SQLITE_URL_PREFIX):
        return database_url

    sqlite_path = database_url[len(_SQLITE_URL_PREFIX) :]
    if sqlite_path == ":memory:":
        return database_url

    path = Path(sqlite_path)
    if not path.is_absolute():
        path = (Path.cwd() / path).resolve()
    else:
        path = path.resolve()

    return f"{_SQLITE_URL_PREFIX}{path.as_posix()}"
