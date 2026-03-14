from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.api.router import api_router
from app.core.config import settings
from app.core.exceptions import register_exception_handlers
from app.db.init_db import init_db
from app.core.logging import setup_logging

@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Application lifespan handler.

    Phase 1 responsibilities:
    - initialize database schema
    - reserve a single place for future startup/shutdown hooks
    """
    init_db()
    yield


def create_app() -> FastAPI:
    setup_logging()
    
    app = FastAPI(
        title=settings.app_name,
        version="0.1.0",
        debug=settings.debug,
        docs_url=settings.docs_url,
        redoc_url=settings.redoc_url,
        openapi_url=settings.openapi_url,
        lifespan=lifespan,
    )

    register_exception_handlers(app)
    app.include_router(api_router, prefix="/api")

    return app


app = create_app()