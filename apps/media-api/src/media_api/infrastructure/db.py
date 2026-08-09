from __future__ import annotations

import sqlite3

from sqlalchemy import Engine, create_engine, event
from sqlalchemy.orm import Session, sessionmaker

from media_api.infrastructure.config import BootstrapSettings


def create_engine_and_session(settings: BootstrapSettings) -> sessionmaker[Session]:
    database_path = settings.database_path.resolve()
    engine = create_engine(
        f"sqlite+pysqlite:///{database_path.as_posix()}",
        future=True,
    )
    _enable_sqlite_foreign_keys(engine)

    return sessionmaker(
        bind=engine,
        autoflush=False,
        expire_on_commit=False,
        class_=Session,
    )


def _enable_sqlite_foreign_keys(engine: Engine) -> None:
    @event.listens_for(engine, "connect")
    def set_sqlite_pragma(
        dbapi_connection: sqlite3.Connection,
        _connection_record: object,
    ) -> None:
        cursor = dbapi_connection.cursor()
        try:
            cursor.execute("PRAGMA foreign_keys=ON")
        finally:
            cursor.close()
