from __future__ import annotations

from fastapi import APIRouter

from app.api.schemas import HealthzRead, PingRead, RuntimeStatusRead
from app.core.responses import ApiResponse, success_response
from app.core.runtime_status import get_runtime_status_read


router = APIRouter()


@router.get("/ping", tags=["system"], response_model=ApiResponse[PingRead])
def ping() -> ApiResponse[PingRead]:
    return success_response(data=PingRead(message="pong"), message="Ping OK.")


@router.get("/healthz", tags=["system"], response_model=ApiResponse[HealthzRead])
def healthz() -> ApiResponse[HealthzRead]:
    return success_response(data=HealthzRead(status="ok"), message="Health check OK.")


@router.get("/system/status", tags=["system"], response_model=ApiResponse[RuntimeStatusRead])
def get_runtime_status() -> ApiResponse[RuntimeStatusRead]:
    return success_response(
        data=get_runtime_status_read(),
        message="Runtime status fetched.",
    )
