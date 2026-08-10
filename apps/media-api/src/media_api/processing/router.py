from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Header, HTTPException
from pydantic import BaseModel
from sqlalchemy.orm import Session, sessionmaker

from media_api.processing.models import ProcessingJob
from media_api.processing.planner import (
    NoApplicableProcessor,
    ProcessingCommandError,
    ProcessingCommandService,
    ProcessingIdempotencyKeyReused,
    ProcessingItemNotFound,
    ProcessingPlanError,
)
from media_api.processing.registry import ProcessorRegistry


class ProcessingJobRead(BaseModel):
    id: str
    inbox_item_id: str
    status: str
    attempt_count: int
    current_step: int
    error_code: str | None
    user_message: str | None

    @classmethod
    def from_model(cls, job: ProcessingJob) -> ProcessingJobRead:
        return cls(
            id=job.id,
            inbox_item_id=job.inbox_item_id,
            status=job.status.value,
            attempt_count=job.attempt_count,
            current_step=job.current_step,
            error_code=job.error_code,
            user_message=job.user_message,
        )


def build_processing_router(
    session_factory: sessionmaker[Session],
    registry: ProcessorRegistry,
) -> APIRouter:
    router = APIRouter(prefix="/api/inbox/items", tags=["processing"])

    @router.post("/{item_id}/process", response_model=ProcessingJobRead)
    def process_item(
        item_id: str,
        idempotency_key: Annotated[str, Header(alias="Idempotency-Key")],
    ) -> ProcessingJobRead:
        with session_factory() as session:
            try:
                job = ProcessingCommandService(
                    session=session,
                    registry=registry,
                ).create_job(item_id, idempotency_key)
            except ProcessingItemNotFound as exc:
                raise HTTPException(status_code=404, detail="Inbox item not found") from exc
            except ProcessingIdempotencyKeyReused as exc:
                raise HTTPException(status_code=409, detail=str(exc)) from exc
            except (NoApplicableProcessor, ProcessingPlanError, ProcessingCommandError) as exc:
                raise HTTPException(status_code=409, detail=str(exc)) from exc
            return ProcessingJobRead.from_model(job)

    return router
