from __future__ import annotations

from fastapi import FastAPI

from media_api.infrastructure.config import BootstrapSettings


def create_app(settings: BootstrapSettings | None = None) -> FastAPI:
    resolved = settings or BootstrapSettings()
    app = FastAPI(title="TraceFold Media API")
    app.state.settings = resolved

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"service": "tracefold-media-api", "status": "ok"}

    return app
