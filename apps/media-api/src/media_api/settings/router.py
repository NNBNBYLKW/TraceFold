from __future__ import annotations

from fastapi import APIRouter, HTTPException
from sqlalchemy.orm import Session, sessionmaker

from media_api.api.dependencies import RuntimeState
from media_api.api.errors import domain_api_error
from media_api.infrastructure.config import BootstrapSettings
from media_api.settings.schemas import (
    RuntimeSettingsPatch,
    RuntimeSettingsRead,
    SystemStatusRead,
)
from media_api.settings.service import SettingsBusy, SettingsService
from media_api.settings.service import SettingsProcessorNotFound
from media_api.processing.registry import ProcessorRegistry
from media_api.storage.local import LocalStorage


def build_settings_router(
    session_factory: sessionmaker[Session],
    bootstrap: BootstrapSettings,
    storage: LocalStorage,
    registry: ProcessorRegistry,
    runtime_state: RuntimeState,
) -> APIRouter:
    router = APIRouter(prefix="/api", tags=["settings"])

    def service(session: Session) -> SettingsService:
        return SettingsService(
            session=session,
            bootstrap=bootstrap,
            storage=storage,
            runtime_state=runtime_state,
            processor_names={
                processor.descriptor.name for processor in registry.snapshot()
            },
        )

    @router.get("/settings", response_model=RuntimeSettingsRead)
    def get_settings() -> RuntimeSettingsRead:
        with session_factory() as session:
            return service(session).get()

    @router.patch("/settings", response_model=RuntimeSettingsRead)
    def update_settings(payload: RuntimeSettingsPatch) -> RuntimeSettingsRead:
        with session_factory() as session:
            try:
                return service(session).update(payload)
            except SettingsBusy as exc:
                raise domain_api_error(exc) from exc
            except SettingsProcessorNotFound as exc:
                raise domain_api_error(exc) from exc

    @router.get("/system/status", response_model=SystemStatusRead)
    def get_system_status() -> SystemStatusRead:
        with session_factory() as session:
            return service(session).system_status()

    return router
