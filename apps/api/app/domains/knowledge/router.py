from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.domains.ai_derivations.schemas import AiDerivationResultListRead
from app.db.session import get_db
from app.domains.knowledge import service
from app.domains.knowledge.schemas import KnowledgeDetailRead, KnowledgeListRead
from app.domains.workbench import service as workbench_service


router = APIRouter()


@router.get("", response_model=ApiResponse[KnowledgeListRead])
def list_knowledge(
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    keyword: str | None = None,
    has_source_text: bool | None = None,
    db: Session = Depends(get_db),
) -> ApiResponse[KnowledgeListRead]:
    result = service.list_knowledge_reads(
        db,
        page=page,
        page_size=page_size,
        sort_by=sort_by,
        sort_order=sort_order,
        date_from=date_from,
        date_to=date_to,
        keyword=keyword,
        has_source_text=has_source_text,
    )
    return success_response(data=result, message="Knowledge entries fetched.")


@router.get("/{knowledge_id}", response_model=ApiResponse[KnowledgeDetailRead])
def get_knowledge(
    knowledge_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[KnowledgeDetailRead]:
    result = service.get_knowledge_read(db, knowledge_id)
    workbench_service.record_knowledge_view_best_effort(db, knowledge_read=result)
    return success_response(data=result, message="Knowledge entry fetched.")


@router.post("/{knowledge_id}/ai/knowledge-summary/rerun", response_model=ApiResponse[AiDerivationResultListRead])
def rerun_knowledge_entry_ai_summary(
    knowledge_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationResultListRead]:
    result = service.rerun_knowledge_summary(db, knowledge_id=knowledge_id)
    return success_response(data=result, message="Knowledge AI summary rerun completed.")
