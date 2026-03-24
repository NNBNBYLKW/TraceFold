from __future__ import annotations

import logging
from pathlib import Path

from alembic import command
from alembic.config import Config
from alembic.script import ScriptDirectory
from alembic.util.exc import CommandError
from alembic.runtime.migration import MigrationContext
from sqlalchemy import create_engine
from sqlalchemy.exc import OperationalError

from app.core.config import settings
from app.core.exceptions import DatabaseUnavailableError, MigrationStateError
from app.core.logging import build_log_message, get_logger, log_event
from app.db.database_url import resolve_database_url


API_ROOT_DIR = Path(__file__).resolve().parents[2]
ALEMBIC_INI_PATH = API_ROOT_DIR / "alembic.ini"
logger = get_logger(__name__)


def _resolve_database_url(database_url: str | None = None) -> str:
    return resolve_database_url(database_url or settings.api_db_url)


def get_alembic_config(database_url: str | None = None) -> Config:
    """
    Build a programmatic Alembic config bound to the shared API DB URL.
    """

    config = Config(str(ALEMBIC_INI_PATH))
    config.set_main_option("script_location", str(API_ROOT_DIR / "migrations"))
    config.set_main_option("sqlalchemy.url", _resolve_database_url(database_url))
    return config


def upgrade_database(revision: str = "head", database_url: str | None = None) -> None:
    """
    Upgrade the configured database to the target Alembic revision.
    """
    try:
        command.upgrade(get_alembic_config(database_url), revision)
        log_event(
            logger,
            level=logging.INFO,
            event="migration_upgrade_completed",
            target_revision=revision,
            backend=_database_backend(database_url),
        )
    except OperationalError as exc:
        logger.exception(
            build_log_message(
                "migration_upgrade_failed",
                reason="database_unavailable",
                target_revision=revision,
                backend=_database_backend(database_url),
            )
        )
        raise DatabaseUnavailableError(
            message="Database is unavailable during migration upgrade.",
            details={"target_revision": revision},
        ) from exc
    except CommandError as exc:
        logger.exception(
            build_log_message(
                "migration_upgrade_failed",
                reason="migration_state_error",
                target_revision=revision,
                backend=_database_backend(database_url),
            )
        )
        raise MigrationStateError(
            details={"target_revision": revision, "error": str(exc)},
        ) from exc


def downgrade_database(revision: str, database_url: str | None = None) -> None:
    """
    Downgrade the configured database to the target Alembic revision.
    """
    try:
        command.downgrade(get_alembic_config(database_url), revision)
        log_event(
            logger,
            level=logging.INFO,
            event="migration_downgrade_completed",
            target_revision=revision,
            backend=_database_backend(database_url),
        )
    except OperationalError as exc:
        logger.exception(
            build_log_message(
                "migration_downgrade_failed",
                reason="database_unavailable",
                target_revision=revision,
                backend=_database_backend(database_url),
            )
        )
        raise DatabaseUnavailableError(
            message="Database is unavailable during migration downgrade.",
            details={"target_revision": revision},
        ) from exc
    except CommandError as exc:
        logger.exception(
            build_log_message(
                "migration_downgrade_failed",
                reason="migration_state_error",
                target_revision=revision,
                backend=_database_backend(database_url),
            )
        )
        raise MigrationStateError(
            details={"target_revision": revision, "error": str(exc)},
        ) from exc


def get_current_revision(database_url: str | None = None) -> str | None:
    """
    Inspect the current Alembic revision stored in the target database.
    """

    url = _resolve_database_url(database_url)
    connect_args: dict[str, object] = {}

    if url.startswith("sqlite"):
        connect_args["check_same_thread"] = False

    engine = create_engine(url, future=True, connect_args=connect_args)
    try:
        with engine.connect() as connection:
            context = MigrationContext.configure(connection)
            return context.get_current_revision()
    except OperationalError as exc:
        raise DatabaseUnavailableError(
            details={"operation": "get_current_revision"},
        ) from exc
    finally:
        engine.dispose()


def get_head_revision(database_url: str | None = None) -> str | None:
    """
    Read the latest revision declared by the local migration script directory.
    """

    try:
        script_directory = ScriptDirectory.from_config(get_alembic_config(database_url))
        return script_directory.get_current_head()
    except CommandError as exc:
        raise MigrationStateError(
            details={"operation": "get_head_revision", "error": str(exc)},
        ) from exc


def _database_backend(database_url: str | None = None) -> str:
    url = _resolve_database_url(database_url)
    return url.split(":", maxsplit=1)[0]
