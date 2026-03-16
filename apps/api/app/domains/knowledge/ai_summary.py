from __future__ import annotations

from collections import Counter
from datetime import UTC, datetime
import re
from typing import Any

from sqlalchemy.orm import Session

from app.domains.ai_derivations.models import AiDerivationResult, AiDerivationStatus
from app.domains.ai_derivations.service import upsert_ai_derivation_result
from app.domains.knowledge.models import KnowledgeEntry


KNOWLEDGE_SUMMARY_DERIVATION_TYPE = "knowledge_summary"
KNOWLEDGE_SUMMARY_MODEL_NAME = "tracefold-knowledge-summary"
KNOWLEDGE_SUMMARY_MODEL_VERSION = "v1"

_KNOWLEDGE_SUMMARY_KEYS = {"summary", "key_points", "keywords"}
_KEYWORD_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]*")
_MAX_KEYWORDS = 5
_MAX_KEY_POINTS = 3
_STOP_WORDS = {
    "a",
    "an",
    "and",
    "are",
    "as",
    "at",
    "be",
    "by",
    "for",
    "from",
    "in",
    "is",
    "it",
    "of",
    "on",
    "or",
    "that",
    "the",
    "this",
    "to",
    "with",
}


def run_knowledge_summary_on_entry_create(
    db: Session,
    *,
    knowledge_entry: KnowledgeEntry,
) -> AiDerivationResult:
    return _generate_knowledge_summary_result(db, knowledge_entry=knowledge_entry)


def rerun_knowledge_summary_for_entry(
    db: Session,
    *,
    knowledge_entry: KnowledgeEntry,
) -> AiDerivationResult:
    return _generate_knowledge_summary_result(db, knowledge_entry=knowledge_entry)


def build_knowledge_summary_content(knowledge_entry: KnowledgeEntry) -> dict[str, Any]:
    title = _normalize_optional_text(knowledge_entry.title)
    content = _normalize_optional_text(knowledge_entry.content)
    source_text = _normalize_optional_text(knowledge_entry.source_text)

    summary_parts: list[str] = []
    if title is not None:
        summary_parts.append(f"This knowledge entry focuses on {title}.")
    elif content is not None:
        summary_parts.append("This knowledge entry captures a saved note.")
    else:
        summary_parts.append("This knowledge entry captures a saved reference.")

    if content is not None:
        summary_parts.append(f"The main content notes: {_truncate_sentence(content)}.")
    elif source_text is not None:
        summary_parts.append("A source excerpt is attached for reference.")

    key_points: list[str] = []
    if title is not None:
        key_points.append(f"Title: {title}.")
    if content is not None:
        key_points.append(f"Content focus: {_truncate_sentence(content)}.")
    if source_text is not None:
        key_points.append(f"Source note available: {_truncate_sentence(source_text)}.")
    if not key_points:
        key_points.append("This entry currently has only minimal formal content.")

    content_json = {
        "summary": " ".join(summary_parts),
        "key_points": key_points[:_MAX_KEY_POINTS],
        "keywords": _extract_keywords(title, content, source_text),
    }
    validate_knowledge_summary_content(content_json)
    return content_json


def validate_knowledge_summary_content(content: dict[str, Any]) -> None:
    if set(content.keys()) != _KNOWLEDGE_SUMMARY_KEYS:
        raise ValueError("knowledge_summary content_json must contain exactly summary, key_points, and keywords.")
    if not isinstance(content["summary"], str) or not content["summary"].strip():
        raise ValueError("knowledge_summary.summary must be a non-empty string.")
    if not isinstance(content["key_points"], list) or any(
        not isinstance(item, str) or not item.strip() for item in content["key_points"]
    ):
        raise ValueError("knowledge_summary.key_points must be a list of non-empty strings.")
    if not isinstance(content["keywords"], list) or any(
        not isinstance(item, str) or not item.strip() for item in content["keywords"]
    ):
        raise ValueError("knowledge_summary.keywords must be a list of non-empty strings.")


def _generate_knowledge_summary_result(
    db: Session,
    *,
    knowledge_entry: KnowledgeEntry,
) -> AiDerivationResult:
    upsert_ai_derivation_result(
        db,
        target_domain="knowledge",
        target_record_id=knowledge_entry.id,
        derivation_type=KNOWLEDGE_SUMMARY_DERIVATION_TYPE,
        status=AiDerivationStatus.PENDING,
        model_name=KNOWLEDGE_SUMMARY_MODEL_NAME,
        model_version=KNOWLEDGE_SUMMARY_MODEL_VERSION,
        generated_at=None,
        failed_at=None,
        content_json=None,
        error_message=None,
    )

    try:
        content = build_knowledge_summary_content(knowledge_entry)
    except Exception as exc:
        return upsert_ai_derivation_result(
            db,
            target_domain="knowledge",
            target_record_id=knowledge_entry.id,
            derivation_type=KNOWLEDGE_SUMMARY_DERIVATION_TYPE,
            status=AiDerivationStatus.FAILED,
            model_name=KNOWLEDGE_SUMMARY_MODEL_NAME,
            model_version=KNOWLEDGE_SUMMARY_MODEL_VERSION,
            generated_at=None,
            failed_at=_utcnow(),
            content_json=None,
            error_message=_build_error_message(exc),
        )

    return upsert_ai_derivation_result(
        db,
        target_domain="knowledge",
        target_record_id=knowledge_entry.id,
        derivation_type=KNOWLEDGE_SUMMARY_DERIVATION_TYPE,
        status=AiDerivationStatus.COMPLETED,
        model_name=KNOWLEDGE_SUMMARY_MODEL_NAME,
        model_version=KNOWLEDGE_SUMMARY_MODEL_VERSION,
        generated_at=_utcnow(),
        failed_at=None,
        content_json=content,
        error_message=None,
    )


def _extract_keywords(title: str | None, content: str | None, source_text: str | None) -> list[str]:
    candidates = " ".join(value for value in (title, content, source_text) if value)
    if not candidates:
        return ["knowledge"]

    counter: Counter[str] = Counter()
    original_forms: dict[str, str] = {}
    for match in _KEYWORD_PATTERN.finditer(candidates):
        original = match.group(0)
        normalized = original.lower()
        if normalized in _STOP_WORDS or len(normalized) < 3:
            continue
        counter[normalized] += 1
        original_forms.setdefault(normalized, original)

    if not counter:
        return ["knowledge"]

    ordered = sorted(counter.items(), key=lambda item: (-item[1], item[0]))
    return [original_forms[key] for key, _ in ordered[:_MAX_KEYWORDS]]


def _truncate_sentence(value: str, limit: int = 120) -> str:
    if len(value) <= limit:
        return value
    return value[: limit - 3].rstrip() + "..."


def _normalize_optional_text(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _build_error_message(exc: Exception) -> str:
    message = _normalize_optional_text(str(exc))
    return message or "Knowledge AI summary generation failed."


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
