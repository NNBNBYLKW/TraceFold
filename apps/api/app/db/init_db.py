from __future__ import annotations

import logging

from app.core.logging import build_log_message, get_logger, log_event
from app.db.migrations import upgrade_database


logger = get_logger(__name__)


def init_db(database_url: str | None = None) -> None:
    """
    Upgrade the configured database to the current formal schema baseline.

    Rules:
    - This function is a compatibility bootstrap entry used by app startup.
    - Formal schema evolution is now owned by Alembic revisions under
      `apps/api/migrations/`.
    - This function must not seed business data.
    - Direct `Base.metadata.create_all()` is reserved for isolated tests only.
    """

    try:
        upgrade_database("head", database_url=database_url)
        log_event(
            logger,
            level=logging.INFO,
            event="runtime_schema_bootstrap_completed",
            target_revision="head",
        )
    except Exception:
        logger.exception(
            build_log_message(
                "runtime_schema_bootstrap_failed",
                target_revision="head",
            )
        )
        raise
