from __future__ import annotations

from collections.abc import Generator

from sqlalchemy import create_engine
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.config import settings
from app.db.database_url import resolve_database_url


def build_engine(database_url: str | None = None) -> Engine:
    """
    Create a SQLAlchemy engine using the shared API DB configuration.

    For SQLite, `check_same_thread=False` is required when the connection
    may be used across different threads in the app runtime.
    """
    resolved_database_url = resolve_database_url(database_url or settings.api_db_url)
    connect_args: dict[str, object] = {}

    if resolved_database_url.startswith("sqlite"):
        connect_args["check_same_thread"] = False

    return create_engine(
        resolved_database_url,
        echo=False,
        future=True,
        connect_args=connect_args,
    )


def build_session_local(database_url: str | None = None):
    engine = build_engine(database_url)
    return sessionmaker(
        bind=engine,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        class_=Session,
    )


engine: Engine = build_engine()

SessionLocal = build_session_local()


def get_db() -> Generator[Session, None, None]:
    """
    FastAPI dependency for obtaining a database session.

    Usage:
        db: Session = Depends(get_db)
    """
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
