from __future__ import annotations

from alembic import context
from sqlalchemy import engine_from_config, pool

from media_api.infrastructure.db import Base

# Register every isolated media model with the shared metadata.
import media_api.catalog.models  # noqa: F401, E402
import media_api.intake.models  # noqa: F401, E402
import media_api.processing.models  # noqa: F401, E402
import media_api.settings.models  # noqa: F401, E402
import media_api.taxonomy.models  # noqa: F401, E402


config = context.config
target_metadata = Base.metadata


def run_migrations_offline() -> None:
    url = config.get_main_option("sqlalchemy.url")
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
        compare_type=True,
        render_as_batch=url.startswith("sqlite"),
    )

    with context.begin_transaction():
        context.run_migrations()


def run_migrations_online() -> None:
    configuration = config.get_section(config.config_ini_section, {})
    connectable = engine_from_config(
        configuration,
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
        future=True,
        connect_args={"check_same_thread": False},
    )

    with connectable.connect() as connection:
        connection.exec_driver_sql("PRAGMA foreign_keys=ON")
        connection.commit()
        context.configure(
            connection=connection,
            target_metadata=target_metadata,
            compare_type=True,
            render_as_batch=True,
        )

        with context.begin_transaction():
            context.run_migrations()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
