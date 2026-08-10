from __future__ import annotations

from fastapi import FastAPI

from media_api.catalog.router import build_catalog_router
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import create_engine_and_session
from media_api.intake.router import build_inbox_router
from media_api.processing.registry import ProcessorRegistry
from media_api.processing.router import build_processing_router
from media_api.processing.worker import PersistentWorker
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage
from media_api.taxonomy.router import build_taxonomy_router


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
    app.state.processor_registry = ProcessorRegistry()
    app.state.processing_worker = PersistentWorker(
        session_factory=app.state.session_factory,
        registry=app.state.processor_registry,
        work_dir=resolved.work_dir,
    )
    app.state.processing_worker.recover_interrupted()

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"service": "tracefold-media-api", "status": "ok"}

    app.include_router(
        build_inbox_router(
            app.state.session_factory,
            app.state.storage,
        )
    )
    app.include_router(build_taxonomy_router(app.state.session_factory))
    app.include_router(
        build_processing_router(
            app.state.session_factory,
            app.state.processor_registry,
        )
    )
    app.include_router(
        build_catalog_router(
            app.state.session_factory,
            app.state.storage,
        )
    )

    return app
