from __future__ import annotations

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from media_api.api.dependencies import RuntimeState
from media_api.api.errors import install_error_contract
from media_api.api.events import EventBus, build_events_router
from media_api.catalog.recovery import IngestRecoveryService
from media_api.catalog.router import build_catalog_router
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import create_engine_and_session
from media_api.infrastructure.lifecycle import RuntimeCoordinator, RuntimeLoopSettings
from media_api.intake.router import build_inbox_router
from media_api.intake.service import ScanService
from media_api.processing.registry import ProcessorRegistry
from media_api.processing.processors.mp4_m4a import Mp4M4aMergeProcessor
from media_api.processing.router import build_processing_router
from media_api.processing.worker import PersistentWorker
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage
from media_api.settings.router import build_settings_router
from media_api.settings.service import SettingsService
from media_api.taxonomy.router import build_taxonomy_router


def create_app(settings: BootstrapSettings | None = None) -> FastAPI:
    resolved = settings or BootstrapSettings()
    runtime_state = RuntimeState()
    event_bus = EventBus()
    session_factory = create_engine_and_session(resolved)
    storage = LocalStorage(resolved)
    storage.provision_directories(
        RuntimePaths(
            inbox_dir=resolved.inbox_dir,
            library_dir=resolved.library_dir,
            work_dir=resolved.work_dir,
        )
    )
    registry = ProcessorRegistry([Mp4M4aMergeProcessor()])
    processing_worker = PersistentWorker(
        session_factory=session_factory,
        registry=registry,
        work_dir=resolved.work_dir,
    )

    def recover_ingests() -> object:
        with session_factory() as session:
            current = SettingsService(
                session=session,
                bootstrap=resolved,
                storage=storage,
            ).get()
            storage.reconfigure(
                BootstrapSettings.model_validate(
                    {
                        **resolved.model_dump(),
                        "inbox_dir": current.inbox_dir,
                        "library_dir": current.library_dir,
                        "work_dir": current.work_dir,
                        "scan_interval_seconds": current.scan_interval_seconds,
                    }
                )
            )
            return IngestRecoveryService(
                session=session,
                storage=storage,
            ).recover_all()

    def run_worker_once() -> object:
        processing_worker.reconfigure_work_dir(storage.work_dir)
        return processing_worker.run_once()

    def scan_once() -> object:
        with session_factory() as session:
            return ScanService(
                session=session,
                storage=storage,
                registry=registry,
            ).scan()

    def load_loop_settings() -> RuntimeLoopSettings:
        with session_factory() as session:
            current = SettingsService(
                session=session,
                bootstrap=resolved,
                storage=storage,
            ).get()
            return RuntimeLoopSettings(
                automatic_scan_enabled=current.automatic_scan_enabled,
                scan_interval_seconds=current.scan_interval_seconds,
            )

    coordinator = RuntimeCoordinator(
        state=runtime_state,
        event_bus=event_bus,
        recover_ingests=recover_ingests,
        recover_processing_jobs=processing_worker.recover_interrupted,
        run_worker_once=run_worker_once,
        scan_once=scan_once,
        load_loop_settings=load_loop_settings,
        dispose=session_factory.kw["bind"].dispose,
        idle_poll_seconds=1.0,
    )
    app = FastAPI(
        title="TraceFold Media API",
        lifespan=coordinator.lifespan,
    )
    install_error_contract(app)
    origins = (
        [resolved.web_origin]
        if resolved.web_origin is not None
        else ["http://127.0.0.1:3100", "http://localhost:3100"]
    )
    app.add_middleware(
        CORSMiddleware,
        allow_origins=origins,
        allow_credentials=False,
        allow_methods=["GET", "POST", "PATCH", "OPTIONS"],
        allow_headers=["Content-Type", "Idempotency-Key", "X-Correlation-ID"],
    )
    app.state.settings = resolved
    app.state.runtime_state = runtime_state
    app.state.event_bus = event_bus
    app.state.session_factory = session_factory
    app.state.storage = storage
    app.state.processor_registry = registry
    app.state.processing_worker = processing_worker
    app.state.runtime_coordinator = coordinator

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"service": "tracefold-media-api", "status": "ok"}

    app.include_router(
        build_inbox_router(
            app.state.session_factory,
            app.state.storage,
            app.state.processor_registry,
            app.state.event_bus,
            runtime_state=app.state.runtime_state,
        )
    )
    app.include_router(build_events_router(app.state.event_bus))
    app.include_router(build_taxonomy_router(app.state.session_factory))
    app.include_router(
        build_settings_router(
            app.state.session_factory,
            resolved,
            app.state.storage,
            app.state.processor_registry,
            app.state.runtime_state,
        )
    )
    app.include_router(
        build_processing_router(
            app.state.session_factory,
            app.state.processor_registry,
            app.state.event_bus,
        )
    )
    app.include_router(
        build_catalog_router(
            app.state.session_factory,
            app.state.storage,
            runtime_state=app.state.runtime_state,
            event_bus=app.state.event_bus,
        )
    )

    return app
