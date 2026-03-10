from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.api.router import api_router
from app.core.exceptions import register_exception_handlers
from app.core.config import settings
from app.db.init_db import init_db


@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Application lifespan handler.

    Phase 1 responsibilities:
    - initialize database schema
    - reserve a single place for future startup/shutdown hooks
    """
    # init_db()
    yield


def create_app() -> FastAPI:
    app = FastAPI(
        title="TraceFold API",
        version="0.1.0",
        lifespan=lifespan,
    )

    register_exception_handlers(app)
    app.include_router(api_router, prefix="/api")

    return app


app = create_app()