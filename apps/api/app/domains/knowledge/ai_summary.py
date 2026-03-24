from __future__ import annotations

from collections import Counter
import re
from typing import Any

from app.ai import service as ai_service


KNOWLEDGE_SUMMARY_DERIVATION_TYPE = "knowledge_summary"

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


def build_knowledge_summary_content(knowledge_entry: Any) -> dict[str, Any]:
    content_json = ai_service.generate_knowledge_summary_content(knowledge_entry)
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
