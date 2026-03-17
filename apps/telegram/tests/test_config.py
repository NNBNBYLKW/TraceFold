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
