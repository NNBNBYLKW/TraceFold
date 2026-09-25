from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.core.config import Settings


def test_debug_is_off_without_an_explicit_development_opt_in(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("TRACEFOLD_API_DEBUG", raising=False)
    settings = Settings(_env_file=None, TRACEFOLD_API_ENV="development")

    assert settings.debug is False


def test_production_rejects_explicit_debug_mode() -> None:
    with pytest.raises(ValidationError):
        Settings(
            _env_file=None,
            TRACEFOLD_API_ENV="production",
            TRACEFOLD_API_DEBUG=True,
        )


def test_production_with_debug_off_is_allowed() -> None:
    settings = Settings(
        _env_file=None,
        TRACEFOLD_API_ENV="production",
        TRACEFOLD_API_DEBUG=False,
    )

    assert settings.is_production
    assert settings.debug is False
