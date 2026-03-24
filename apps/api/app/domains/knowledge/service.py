from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import AppException, BadRequestError, DerivationFailedError, NotFoundError
from app.core.logging import build_log_message, get_logger, log_event
from app.domains.ai_derivations.schemas import AiDerivationResultListRead
from app.domains.knowledge import repository
from app.domains.knowledge.models import KnowledgeEntry
from app.domains.knowledge.schemas import KnowledgeDetailRead, KnowledgeListItemRead, KnowledgeListRead


_DEFAULT_SORT_BY = "created_at"
_ALLOWED_SORT_FIELDS = {"created_at", "title"}
_ALLOWED_SORT_ORDERS = {"asc", "desc"}
_PREVIEW_LENGTH = 120
_UNTITLED = "(untitled)"
logger = get_logger(__name__)


def create_knowledge_entry(
    db: Session,
    *,
    source_capture_id: int,
    source_pending_id: int | None,
    payload: dict,
) -> KnowledgeEntry:
    entry = KnowledgeEntry(
        source_capture_id=source_capture_id,
        source_pending_id=source_pending_id,
        title=payload.get("title"),
        content=payload.get("content"),
        source_text=payload.get("source_text"),
    )
    db.add(entry)
    db.flush()
    from app.domains.ai_derivations import service as ai_derivations_service

    ai_derivations_service.generate_knowledge_summary_now(db, knowledge_entry=entry)
    return entry


def list_knowledge_reads(
    db: Session,
    *,
    page: int = 1,
    page_size: int = 20,
    sort_by: str | None = None,
    sort_order: str = "desc",
    date_from: datetime | None = None,
    date_to: datetime | None = None,
    keyword: str | None = None,
    has_source_text: bool | None = None,
) -> KnowledgeListRead:
    validated_page, validated_page_size = _validate_pagination(page=page, page_size=page_size)
    validated_sort_by = _validate_sort_by(sort_by, default=_DEFAULT_SORT_BY)
    validated_sort_order = _validate_sort_order(sort_order)
    _validate_date_range(date_from=date_from, date_to=date_to)

    entries, total = repository.list_knowledge_entries(
        db,
        page=validated_page,
        page_size=validated_page_size,
        sort_by=validated_sort_by,
        sort_order=validated_sort_order,
        date_from=date_from,
        date_to=date_to,
        keyword=_normalize_optional_text(keyword),
        has_source_text=has_source_text,
    )
    return KnowledgeListRead(
        items=[_build_knowledge_list_item(entry) for entry in entries],
        page=validated_page,
        page_size=validated_page_size,
        total=total,
    )


def get_knowledge_read(db: Session, knowledge_id: int) -> KnowledgeDetailRead:
    entry = repository.get_knowledge_entry_by_id(db, knowledge_id)
    if entry is None:
        log_event(
            logger,
            level=30,
            event="knowledge_read_failed",
            domain="knowledge",
            action="read",
            target_type="knowledge_entry",
            target_id=knowledge_id,
            result="not_found",
            error_code="KNOWLEDGE_NOT_FOUND",
        )
        raise NotFoundError(
            message=f"Knowledge entry {knowledge_id} was not found.",
            code="KNOWLEDGE_NOT_FOUND",
        )
    return KnowledgeDetailRead(
        id=entry.id,
        created_at=entry.created_at,
        title=_display_title(entry.title),
        content=entry.content,
        source_text=entry.source_text,
        source_capture_id=entry.source_capture_id,
        source_pending_id=entry.source_pending_id,
    )


def rerun_knowledge_summary(
    db: Session,
    *,
    knowledge_id: int,
) -> AiDerivationResultListRead:
    try:
        entry = repository.get_knowledge_entry_by_id(db, knowledge_id)
        if entry is None:
            raise NotFoundError(
                message=f"Knowledge entry {knowledge_id} was not found.",
                code="KNOWLEDGE_NOT_FOUND",
            )

        rerun_knowledge_summary_for_entry(db, knowledge_entry=entry)
        db.commit()
        return _build_ai_derivation_result_list_read(db, knowledge_id=entry.id)
    except Exception as exc:
        db.rollback()
        if isinstance(exc, AppException):
            raise
        logger.exception(
            build_log_message(
                "knowledge_ai_summary_rerun_failed",
                domain="knowledge",
                knowledge_id=knowledge_id,
                error_code=ErrorCode.DERIVATION_FAILED,
            )
        )
        raise DerivationFailedError(
            message="Knowledge AI summary rerun failed.",
            details={"target_domain": "knowledge", "target_id": knowledge_id},
        ) from exc


def rerun_knowledge_summary_for_entry(
    db: Session,
    *,
    knowledge_entry: KnowledgeEntry,
) -> None:
    from app.domains.ai_derivations import service as ai_derivations_service

    ai_derivations_service.generate_knowledge_summary_now(
        db,
        knowledge_entry=knowledge_entry,
        raise_on_failure=False,
    )


def request_knowledge_summary_recompute(
    db: Session,
    *,
    knowledge_id: int,
) -> dict[str, Any]:
    from app.domains.ai_derivations import service as ai_derivations_service

    return ai_derivations_service.request_knowledge_summary_recompute(
        db,
        knowledge_id=knowledge_id,
    )


def _build_knowledge_list_item(entry: KnowledgeEntry) -> KnowledgeListItemRead:
    return KnowledgeListItemRead(
        id=entry.id,
        created_at=entry.created_at,
        display_title=_display_title(entry.title),
        content_preview=_build_preview(entry.content),
        has_source_text=_normalize_optional_text(entry.source_text) is not None,
        has_source_pending=entry.source_pending_id is not None,
    )


def _build_ai_derivation_result_list_read(
    db: Session,
    *,
    knowledge_id: int,
) -> AiDerivationResultListRead:
    from app.domains.ai_derivations.service import list_ai_derivation_reads

    return list_ai_derivation_reads(
        db,
        target_domain="knowledge",
        target_record_id=knowledge_id,
    )


def _display_title(value: str | None) -> str:
    normalized = _normalize_optional_text(value)
    return normalized or _UNTITLED


def _build_preview(value: str | None) -> str | None:
    normalized = _normalize_optional_text(value)
    if normalized is None:
        return None
    if len(normalized) <= _PREVIEW_LENGTH:
        return normalized
    return normalized[: _PREVIEW_LENGTH - 3].rstrip() + "..."


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _validate_pagination(*, page: int, page_size: int) -> tuple[int, int]:
    if page < 1:
        raise BadRequestError(
            message="page must be greater than or equal to 1.",
            code="INVALID_PAGE",
        )
    if page_size < 1 or page_size > 100:
        raise BadRequestError(
            message="page_size must be between 1 and 100.",
            code="INVALID_PAGE_SIZE",
        )
    return page, page_size


def _validate_sort_by(sort_by: str | None, *, default: str) -> str:
    if sort_by is None:
        return default
    normalized_sort_by = sort_by.strip()
    if normalized_sort_by not in _ALLOWED_SORT_FIELDS:
        allowed = ", ".join(sorted(_ALLOWED_SORT_FIELDS))
        raise BadRequestError(
            message=f"sort_by must be one of: {allowed}.",
            code="INVALID_SORT_BY",
        )
    return normalized_sort_by


def _validate_sort_order(sort_order: str) -> str:
    normalized_sort_order = sort_order.strip().lower()
    if normalized_sort_order not in _ALLOWED_SORT_ORDERS:
        raise BadRequestError(
            message="sort_order must be either asc or desc.",
            code="INVALID_SORT_ORDER",
        )
    return normalized_sort_order


def _validate_date_range(*, date_from: datetime | None, date_to: datetime | None) -> None:
    if date_from is not None and date_to is not None and date_from > date_to:
        raise BadRequestError(
            message="date_from must be earlier than or equal to date_to.",
            code="INVALID_DATE_RANGE",
        )
