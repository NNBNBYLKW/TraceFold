import pytest
from pydantic import ValidationError

from apps.telegram.app.core.config import TelegramAdapterSettings


def test_settings_load_from_environment(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_BOT_TOKEN", "test-token")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_API_BASE_URL", "http://localhost:8000/api")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS", "7")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_DEBUG", "true")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_LOG_ENABLED", "false")

    settings = TelegramAdapterSettings()

    assert settings.bot_token == "test-token"
    assert settings.api_base_url == "http://localhost:8000/api"
    assert settings.timeout_seconds == 7
    assert settings.debug is True
    assert settings.log_enabled is False


def test_settings_reject_blank_bot_token(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_BOT_TOKEN", "   ")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_API_BASE_URL", "http://localhost:8000/api")

    with pytest.raises(ValidationError):
        TelegramAdapterSettings()


def test_settings_reject_non_http_api_base_url(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_BOT_TOKEN", "test-token")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_API_BASE_URL", "localhost:8000/api")

    with pytest.raises(ValidationError):
        TelegramAdapterSettings()


def test_settings_reject_non_positive_timeout(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_BOT_TOKEN", "test-token")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_API_BASE_URL", "http://localhost:8000/api")
    monkeypatch.setenv("TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS", "0")

    with pytest.raises(ValidationError):
        TelegramAdapterSettings()
