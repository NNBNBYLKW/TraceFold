from __future__ import annotations

from fastapi import APIRouter, BackgroundTasks, Depends, status
from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import BadRequestError
from app.core.responses import ApiResponse, created_response, success_response
from app.core.tasks import submit_background_task
from app.db.session import get_db
from app.domains.ai_derivations import service
from app.domains.ai_derivations.schemas import (
    AiDerivationInvalidateRead,
    AiDerivationListRead,
    AiDerivationRead,
    AiDerivationTaskSubmissionRead,
)
from app.tasks.registry import TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE


router = APIRouter()


@router.get("", response_model=ApiResponse[AiDerivationListRead])
def list_ai_derivations(
    target_type: str | None = None,
    target_id: int | None = None,
    derivation_kind: str | None = None,
    status: str | None = None,
    limit: int = 50,
    target_domain: str | None = None,
    target_record_id: int | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationListRead]:
    result = service.list_ai_derivation_reads(
        db,
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
        status=status,
        limit=limit,
        target_domain=target_domain,
        target_record_id=target_record_id,
    )
    return success_response(data=result, message="AI derivations fetched.")


@router.get("/{target_type}/{target_id}", response_model=ApiResponse[AiDerivationRead])
def get_ai_derivation(
    target_type: str,
    target_id: int,
    derivation_kind: str = service.FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationRead]:
    result = service.get_ai_derivation_read(
        db,
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
    )
    return success_response(data=result, message="AI derivation fetched.")


@router.post(
    "/{target_type}/{target_id}/recompute",
    response_model=ApiResponse[AiDerivationTaskSubmissionRead],
    status_code=status.HTTP_201_CREATED,
)
def recompute_ai_derivation(
    target_type: str,
    target_id: int,
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationTaskSubmissionRead]:
    if target_type != service.FORMAL_TARGET_TYPE_KNOWLEDGE:
        raise BadRequestError(
            message="Only knowledge target_type is supported by the formal AI derivation baseline.",
            code=ErrorCode.INVALID_DERIVATION_TARGET_TYPE,
        )

    payload = service.request_knowledge_summary_recompute(
        db,
        knowledge_id=target_id,
    )
    task_read = submit_background_task(
        background_tasks,
        db,
        task_type=TASK_TYPE_KNOWLEDGE_SUMMARY_RECOMPUTE,
        payload_json=payload,
    )
    service.log_knowledge_summary_recompute_requested(
        task_id=task_read.id,
        knowledge_id=target_id,
    )
    return created_response(
        data=AiDerivationTaskSubmissionRead(
            task_id=task_read.id,
            task_type=task_read.task_type,
            target_type=target_type,
            target_id=target_id,
            derivation_kind=service.FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
            derivation_status="pending",
        ),
        message="AI derivation recompute submitted.",
    )


@router.post(
    "/{target_type}/{target_id}/invalidate",
    response_model=ApiResponse[AiDerivationInvalidateRead],
)
def invalidate_ai_derivation(
    target_type: str,
    target_id: int,
    derivation_kind: str = service.FORMAL_DERIVATION_KIND_KNOWLEDGE_SUMMARY,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationInvalidateRead]:
    result = service.invalidate_ai_derivation(
        db,
        target_type=target_type,
        target_id=target_id,
        derivation_kind=derivation_kind,
    )
    db.commit()
    return success_response(data=result, message="AI derivation invalidated.")
