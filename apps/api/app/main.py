from __future__ import annotations

import re
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from app.api.router import api_router
from app.core.config import settings
from app.core.exceptions import register_exception_handlers
from app.core.logging import setup_logging
from app.core.responses import error_response
from app.db.init_db import init_db


LOCAL_WEB_ORIGINS = (
    "http://127.0.0.1",
    "http://localhost",
    "http://127.0.0.1:3000",
    "http://localhost:3000",
    "http://127.0.0.1:5173",
    "http://localhost:5173",
)
LOOPBACK_HOSTS = frozenset({"127.0.0.1", "localhost", "::1"})
LOOPBACK_AUTHORITY = re.compile(
    r"(?:127\.0\.0\.1|localhost|\[::1\])(?::([0-9]{1,5}))?",
    re.IGNORECASE,
)


def _trusted_loopback_host(authority: str) -> bool:
    match = LOOPBACK_AUTHORITY.fullmatch(authority)
    if match is None:
        return False
    port = match.group(1)
    return port is None or 0 < int(port) <= 65535


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

    app.add_middleware(
        CORSMiddleware,
        allow_origins=LOCAL_WEB_ORIGINS,
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    @app.middleware("http")
    async def guard_local_browser_requests(request: Request, call_next):
        if not _trusted_loopback_host(request.headers.get("host", "")):
            return JSONResponse(
                error_response(
                    message="Invalid host.", code="INVALID_HOST"
                ).model_dump(),
                status_code=400,
            )
        if request.method != "OPTIONS":
            origin = request.headers.get("origin")
            same_loopback_origin = (
                request.url.hostname in LOOPBACK_HOSTS
                and origin == str(request.base_url).rstrip("/")
            )
            # CORS does not stop simple cross-site requests from reaching handlers.
            if (
                origin is not None
                and origin not in LOCAL_WEB_ORIGINS
                and not same_loopback_origin
            ):
                return JSONResponse(
                    error_response(
                        message="Cross-origin request denied.", code="FORBIDDEN"
                    ).model_dump(),
                    status_code=403,
                )
            fetch_site = request.headers.get("sec-fetch-site")
            if origin is None and (
                fetch_site not in (None, "none", "same-origin")
            ):
                return JSONResponse(
                    error_response(
                        message="Cross-origin request denied.", code="FORBIDDEN"
                    ).model_dump(),
                    status_code=403,
                )
        return await call_next(request)
    
    register_exception_handlers(app)
    app.include_router(api_router, prefix="/api")

    return app


app = create_app()
