from __future__ import annotations

from collections.abc import Generator

from sqlalchemy import create_engine
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker

from app.core.config import settings


def _build_engine() -> Engine:
    """
    Create the global SQLAlchemy engine.

    For SQLite, `check_same_thread=False` is required when the connection
    may be used across different threads in the app runtime.
    """
    connect_args: dict[str, object] = {}

    if settings.api_db_url.startswith("sqlite"):
        connect_args["check_same_thread"] = False

    return create_engine(
        settings.api_db_url,
        echo=False,
        future=True,
        connect_args=connect_args,
    )


engine: Engine = _build_engine()

SessionLocal = sessionmaker(
    bind=engine,
    autoflush=False,
    autocommit=False,
    expire_on_commit=False,
    class_=Session,
)


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