import pytest
from pydantic import ValidationError

from apps.feishu.app.core.config import FeishuAdapterSettings


def test_settings_load_from_environment(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_ID", "app-id")
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_SECRET", "app-secret")
    monkeypatch.setenv("TRACEFOLD_FEISHU_API_BASE_URL", "http://localhost:8000/api")
    monkeypatch.setenv("TRACEFOLD_FEISHU_OPEN_BASE_URL", "https://open.feishu.cn/open-apis")
    monkeypatch.setenv("TRACEFOLD_FEISHU_TIMEOUT_SECONDS", "7")
    monkeypatch.setenv("TRACEFOLD_FEISHU_DEBUG", "true")
    monkeypatch.setenv("TRACEFOLD_FEISHU_LOG_ENABLED", "false")

    settings = FeishuAdapterSettings()

    assert settings.app_id == "app-id"
    assert settings.app_secret == "app-secret"
    assert settings.api_base_url == "http://localhost:8000/api"
    assert settings.open_base_url == "https://open.feishu.cn/open-apis"
    assert settings.timeout_seconds == 7
    assert settings.debug is True
    assert settings.log_enabled is False


def test_settings_reject_blank_app_id(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_ID", "   ")
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_SECRET", "secret")
    monkeypatch.setenv("TRACEFOLD_FEISHU_API_BASE_URL", "http://localhost:8000/api")

    with pytest.raises(ValidationError):
        FeishuAdapterSettings()


def test_settings_reject_non_http_api_base_url(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_ID", "app-id")
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_SECRET", "secret")
    monkeypatch.setenv("TRACEFOLD_FEISHU_API_BASE_URL", "localhost:8000/api")

    with pytest.raises(ValidationError):
        FeishuAdapterSettings()


def test_settings_reject_non_positive_timeout(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_ID", "app-id")
    monkeypatch.setenv("TRACEFOLD_FEISHU_APP_SECRET", "secret")
    monkeypatch.setenv("TRACEFOLD_FEISHU_API_BASE_URL", "http://localhost:8000/api")
    monkeypatch.setenv("TRACEFOLD_FEISHU_TIMEOUT_SECONDS", "0")

    with pytest.raises(ValidationError):
        FeishuAdapterSettings()
