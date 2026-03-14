from __future__ import annotations

from fastapi import APIRouter


router = APIRouter()


@router.get("/ping", tags=["system"])
def ping() -> dict[str, str]:
    return {"message": "pong"}


@router.get("/healthz", tags=["system"])
def healthz() -> dict[str, str]:
    return {"status": "ok"}