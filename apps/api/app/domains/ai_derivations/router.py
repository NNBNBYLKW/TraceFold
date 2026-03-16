from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.core.responses import ApiResponse, success_response
from app.db.session import get_db
from app.domains.ai_derivations import service
from app.domains.ai_derivations.schemas import AiDerivationResultListRead


router = APIRouter()


@router.get("", response_model=ApiResponse[AiDerivationResultListRead])
def list_ai_derivations(
    target_domain: str,
    target_record_id: int,
    db: Session = Depends(get_db),
) -> ApiResponse[AiDerivationResultListRead]:
    result = service.list_ai_derivation_reads(
        db,
        target_domain=target_domain,
        target_record_id=target_record_id,
    )
    return success_response(data=result, message="AI derivation results fetched.")
