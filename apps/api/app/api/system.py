from __future__ import annotations

from fastapi import APIRouter

from app.api.schemas import HealthzRead, PingRead
from app.core.responses import ApiResponse, success_response


router = APIRouter()


@router.get("/ping", tags=["system"], response_model=ApiResponse[PingRead])
def ping() -> ApiResponse[PingRead]:
    return success_response(data=PingRead(message="pong"), message="Ping OK.")


@router.get("/healthz", tags=["system"], response_model=ApiResponse[HealthzRead])
def healthz() -> ApiResponse[HealthzRead]:
    return success_response(data=HealthzRead(status="ok"), message="Health check OK.")
