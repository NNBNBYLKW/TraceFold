from __future__ import annotations

from fastapi import FastAPI

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import create_engine_and_session
from media_api.intake.router import build_inbox_router
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage


def create_app(settings: BootstrapSettings | None = None) -> FastAPI:
    resolved = settings or BootstrapSettings()
    app = FastAPI(title="TraceFold Media API")
    app.state.settings = resolved
    app.state.session_factory = create_engine_and_session(resolved)
    app.state.storage = LocalStorage(resolved)
    app.state.storage.provision_directories(
        RuntimePaths(
            inbox_dir=resolved.inbox_dir,
            library_dir=resolved.library_dir,
            work_dir=resolved.work_dir,
        )
    )

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"service": "tracefold-media-api", "status": "ok"}

    app.include_router(
        build_inbox_router(
            app.state.session_factory,
            app.state.storage,
        )
    )

    return app
