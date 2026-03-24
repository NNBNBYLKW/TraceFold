from __future__ import annotations

import json
from typing import Any

import httpx

from app.core.logging import get_logger, log_event
from app.core.exceptions import DependencyUnavailableError


OPENAI_COMPATIBLE_PROVIDER = "openai_compatible"
# Local qwen3.5:9b may spend a large share of completion tokens on reasoning
# before emitting final JSON, so the allowance needs headroom beyond the
# original minimal stub-era value.
_MAX_COMPLETION_TOKENS = 1800
_DEBUG_RESPONSE_TEXT_LIMIT = 1500

logger = get_logger(__name__)


def generate_openai_compatible_json_completion(
    *,
    base_url: str,
    api_key: str | None,
    model: str,
    timeout_seconds: float,
    system_prompt: str,
    user_prompt: str,
) -> dict[str, Any]:
    headers = {"Content-Type": "application/json"}
    if api_key:
        headers["Authorization"] = f"Bearer {api_key}"

    try:
        response = httpx.post(
            f"{base_url.rstrip('/')}/chat/completions",
            headers=headers,
            json={
                "model": model,
                "stream": False,
                "temperature": 0,
                "max_tokens": _MAX_COMPLETION_TOKENS,
                "response_format": {"type": "json_object"},
                "messages": [
                    {"role": "system", "content": system_prompt},
                    {"role": "user", "content": user_prompt},
                ],
            },
            timeout=timeout_seconds,
        )
        response.raise_for_status()
        payload = response.json()
    except httpx.TimeoutException as exc:
        raise DependencyUnavailableError(
            message="AI provider request timed out.",
            details={
                "provider": OPENAI_COMPATIBLE_PROVIDER,
                "base_url": base_url,
                "model": model,
            },
        ) from exc
    except httpx.HTTPStatusError as exc:
        raise DependencyUnavailableError(
            message="AI provider returned a non-2xx response.",
            details={
                "provider": OPENAI_COMPATIBLE_PROVIDER,
                "base_url": base_url,
                "model": model,
                "status_code": exc.response.status_code if exc.response is not None else None,
            },
        ) from exc
    except httpx.RequestError as exc:
        raise DependencyUnavailableError(
            message="AI provider request failed.",
            details={
                "provider": OPENAI_COMPATIBLE_PROVIDER,
                "base_url": base_url,
                "model": model,
            },
        ) from exc
    except ValueError as exc:
        raise DependencyUnavailableError(
            message="AI provider returned invalid response JSON.",
            details={
                "provider": OPENAI_COMPATIBLE_PROVIDER,
                "base_url": base_url,
                "model": model,
            },
        ) from exc

    content = _extract_chat_completion_content(
        payload,
        status_code=response.status_code,
        response_text=response.text,
        model=model,
    )
    parsed = _parse_json_object(content, model=model)
    if not isinstance(parsed, dict):
        raise DependencyUnavailableError(
            message="AI provider returned an invalid JSON payload shape.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER, "model": model},
        )
    return parsed


def _extract_chat_completion_content(
    payload: dict[str, Any],
    *,
    status_code: int,
    response_text: str,
    model: str,
) -> str:
    choices = payload.get("choices")
    if not isinstance(choices, list) or not choices:
        raise DependencyUnavailableError(
            message="AI provider returned no completion choices.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER},
        )

    first_choice = choices[0]
    message = first_choice.get("message")
    if not isinstance(message, dict):
        raise DependencyUnavailableError(
            message="AI provider completion did not include a message payload.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER},
        )

    content = message.get("content")
    if isinstance(content, str):
        normalized_content = content.strip()
        if normalized_content:
            return normalized_content
    if isinstance(content, list):
        text_parts: list[str] = []
        for item in content:
            if isinstance(item, dict) and item.get("type") == "text":
                text_value = _normalize_optional_text(item.get("text"))
                if text_value is not None:
                    text_parts.append(text_value)
        if text_parts:
            return "\n".join(text_parts)

    log_event(
        logger,
        level=40,
        event="ai_provider_empty_completion_content",
        provider=OPENAI_COMPATIBLE_PROVIDER,
        model=model,
        status_code=status_code,
        finish_reason=first_choice.get("finish_reason"),
        message_payload=message,
        response_text_excerpt=response_text[:_DEBUG_RESPONSE_TEXT_LIMIT],
    )
    raise DependencyUnavailableError(
        message="AI provider returned empty completion content.",
        details={
            "provider": OPENAI_COMPATIBLE_PROVIDER,
            "model": model,
            "finish_reason": first_choice.get("finish_reason"),
            "reasoning_present": bool(_normalize_optional_text(message.get("reasoning"))),
        },
    )


def _parse_json_object(content: str, *, model: str) -> Any:
    try:
        return json.loads(content)
    except json.JSONDecodeError as exc:
        raise DependencyUnavailableError(
            message="AI provider returned non-JSON completion content.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER, "model": model},
        ) from exc


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None
