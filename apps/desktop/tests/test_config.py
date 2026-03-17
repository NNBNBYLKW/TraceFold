from apps.desktop.app.core.config import DesktopShellSettings


def test_settings_load_from_environment(monkeypatch):
    monkeypatch.setenv("TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL", "http://localhost:3000")
    monkeypatch.setenv("TRACEFOLD_DESKTOP_API_BASE_URL", "http://localhost:8000/api")
    monkeypatch.setenv("TRACEFOLD_DESKTOP_STARTUP_MODE", "tray")
    monkeypatch.setenv("TRACEFOLD_DESKTOP_DEBUG", "true")
    monkeypatch.setenv("TRACEFOLD_DESKTOP_LOG_ENABLED", "false")

    settings = DesktopShellSettings()

    assert settings.web_workbench_url == "http://localhost:3000"
    assert settings.api_base_url == "http://localhost:8000/api"
    assert settings.startup_mode == "tray"
    assert settings.debug is True
    assert settings.log_enabled is False
