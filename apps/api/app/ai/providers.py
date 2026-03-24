from __future__ import annotations

from typing import Any

from app.ai.openai_compatible import (
    OPENAI_COMPATIBLE_PROVIDER,
    generate_openai_compatible_json_completion,
)
from app.core.config import settings
from app.core.exceptions import BadRequestError, DependencyUnavailableError


def generate_json_completion(
    *,
    system_prompt: str,
    user_prompt: str,
) -> dict[str, Any]:
    provider = get_configured_provider_name()
    if provider != OPENAI_COMPATIBLE_PROVIDER:
        raise DependencyUnavailableError(
            message="AI provider is unavailable for formal derivation generation.",
            details={"provider": provider or "unset"},
        )

    base_url = get_configured_base_url()
    model = get_configured_model_name()
    timeout_seconds = get_configured_timeout_seconds()
    if base_url is None:
        raise DependencyUnavailableError(
            message="AI provider base URL is not configured.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER},
        )
    if model is None:
        raise DependencyUnavailableError(
            message="AI provider model is not configured.",
            details={"provider": OPENAI_COMPATIBLE_PROVIDER},
        )

    return generate_openai_compatible_json_completion(
        base_url=base_url,
        api_key=get_configured_api_key(),
        model=model,
        timeout_seconds=timeout_seconds,
        system_prompt=system_prompt,
        user_prompt=user_prompt,
    )


def get_configured_provider_name() -> str | None:
    return _normalize_optional_text(settings.ai_provider)


def get_configured_base_url() -> str | None:
    return _normalize_optional_text(settings.ai_base_url)


def get_configured_api_key() -> str | None:
    return _normalize_optional_text(settings.ai_api_key)


def get_configured_model_name() -> str | None:
    return _normalize_optional_text(settings.ai_model)


def get_configured_timeout_seconds() -> float:
    return _normalize_timeout_seconds(settings.ai_timeout_seconds)


def _normalize_timeout_seconds(value: int | float) -> float:
    timeout_seconds = float(value)
    if timeout_seconds <= 0:
        raise BadRequestError(
            message="TRACEFOLD_AI_TIMEOUT_SECONDS must be greater than 0.",
        )
    return timeout_seconds


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None
