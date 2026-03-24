from __future__ import annotations

import json
from typing import Any

from app.ai import providers


KNOWLEDGE_SUMMARY_PROMPT_VERSION = "knowledge_summary_json_v3"


def generate_knowledge_summary_content(knowledge_entry: Any) -> dict[str, Any]:
    return providers.generate_json_completion(
        system_prompt=_build_knowledge_summary_system_prompt(),
        user_prompt=_build_knowledge_summary_user_prompt(knowledge_entry),
    )


def get_knowledge_summary_model_metadata() -> dict[str, str | None]:
    provider_name = providers.get_configured_provider_name() or "unknown_provider"
    return {
        "model_key": providers.get_configured_model_name(),
        "model_version": f"{provider_name}:{KNOWLEDGE_SUMMARY_PROMPT_VERSION}",
    }


def _build_knowledge_summary_system_prompt() -> str:
    return (
        'Output only JSON: {"summary":"string","key_points":["string"],"keywords":["string"]}. '
        "Use only the provided record. Keep summary concise."
    )


def _build_knowledge_summary_user_prompt(knowledge_entry: Any) -> str:
    record_payload = {
        key: value
        for key, value in {
            "title": _truncate_text(_normalize_optional_text(getattr(knowledge_entry, "title", None)), limit=200),
            "content": _truncate_text(_normalize_optional_text(getattr(knowledge_entry, "content", None)), limit=1200),
            "source_text": _truncate_text(_normalize_optional_text(getattr(knowledge_entry, "source_text", None)), limit=800),
        }.items()
        if value is not None
    }
    return json.dumps(record_payload, ensure_ascii=False, separators=(",", ":"))


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _truncate_text(value: str | None, *, limit: int) -> str | None:
    if value is None or len(value) <= limit:
        return value
    return value[:limit].rstrip()
