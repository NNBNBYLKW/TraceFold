import pytest
from pydantic import ValidationError

from apps.desktop.app.core.config import DesktopShellSettings
from apps.desktop.app.shell.notifications import NotificationBridgeSkeleton
from apps.desktop.app.shell.app import DesktopShellApp
from apps.desktop.app.shell.tray import TrayIntegrationSkeleton
from apps.desktop.app.shell.window import MainWindowSkeleton


class FakeStatusClient:
    def __init__(self, payload=None, should_fail=False):
        self.payload = payload or {"status": "ok"}
        self.home_payload = {"current_mode": {"template_name": "Home"}}
        self.should_fail = should_fail
        self.closed = False

    def get_status(self):
        if self.should_fail:
            from apps.desktop.app.clients.status_client import DesktopStatusClientError

            raise DesktopStatusClientError("TraceFold API is unavailable.")
        return self.payload

    def close(self):
        self.closed = True

    def get_workbench_home(self):
        if self.should_fail:
            from apps.desktop.app.clients.status_client import DesktopStatusClientError

            raise DesktopStatusClientError("TraceFold API is unavailable.")
        return self.home_payload


def test_desktop_shell_app_wiring_and_bootstrap():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(payload={"status": "ok"})
    app = DesktopShellApp(
        settings=settings,
        status_client=status_client,
        window=MainWindowSkeleton(
            title="TraceFold Desktop Shell",
            workbench_url="http://localhost:3000",
        ),
        tray=TrayIntegrationSkeleton(),
        notifications=NotificationBridgeSkeleton(),
    )

    summary = app.startup_summary()
    bootstrap = app.bootstrap()
    opened = app.open_workbench()
    tray_menu = app.tray_menu_items()
    app.close()

    assert summary["web_workbench_url"] == "http://localhost:3000/workbench"
    assert bootstrap["service_status"] == "ok"
    assert bootstrap["service_last_checked"] is not None
    assert bootstrap["service_error_hint"] is None
    assert bootstrap["tray_visible"] is True
    assert bootstrap["resident"] is True
    assert opened["status"] == "ready"
    assert opened["url"] == "http://localhost:3000/workbench"
    assert app.state.workbench_state == "ready"
    assert app.state.active_workbench_url == "http://localhost:3000/workbench"
    assert app.state.active_mode_name == "Home"
    assert app.state.workbench_status_label == "Current mode: Home"
    assert app.window.workbench_status_label == "Current mode: Home"
    assert app.window.service_status_label == "Service status: available"
    assert app.window.service_status_hint is None
    assert app.state.window_visible is True
    assert tray_menu[0]["label"] == "Open TraceFold"
    assert status_client.closed is True


def test_desktop_shell_app_marks_service_unavailable_when_probe_fails():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(should_fail=True)
    app = DesktopShellApp(settings=settings, status_client=status_client)

    bootstrap = app.bootstrap()

    assert bootstrap["service_status"] == "unavailable"
    assert bootstrap["service_last_checked"] is not None
    assert bootstrap["service_error_hint"] == "Cannot reach TraceFold API."
    assert app.state.service_status == "unavailable"
    assert app.state.service_error_hint == "Cannot reach TraceFold API."
    assert app.window.service_status_label == "Service status: unavailable"
    assert app.window.service_status_hint == "Cannot reach TraceFold API."
    assert len(app.notifications.events) == 1
    assert app.notifications.events[0].title == "TraceFold service unavailable"
    assert app.notifications.events[0].action_key == "open_workbench"


def test_desktop_shell_app_rejects_invalid_workbench_url():
    with pytest.raises(ValidationError):
        DesktopShellSettings.model_validate(
            {
                "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "not-a-url",
                "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
                "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
                "TRACEFOLD_DESKTOP_DEBUG": False,
                "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
            }
        )


def test_desktop_shell_app_can_hide_and_show_window_while_resident():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(payload={"status": "ok"})
    app = DesktopShellApp(settings=settings, status_client=status_client)
    app.bootstrap()
    app.open_workbench()

    hidden = app.hide_window()
    shown = app.show_window()

    assert hidden["window_visible"] is False
    assert hidden["resident"] is True
    assert shown["window_visible"] is True
    assert app.state.resident is True


def test_desktop_shell_app_defaults_root_workbench_url_to_workbench_home():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    app = DesktopShellApp(settings=settings, status_client=FakeStatusClient())

    assert app.state.workbench_url == "http://localhost:3000/workbench"
    assert app.window.workbench_url == "http://localhost:3000/workbench"


def test_desktop_shell_app_can_quit_from_shell_state():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(payload={"status": "ok"})
    app = DesktopShellApp(settings=settings, status_client=status_client)
    app.bootstrap()
    app.open_workbench()

    result = app.quit()

    assert result["quit_requested"] is True
    assert app.state.quit_requested is True
    assert app.state.window_visible is False
    assert app.state.tray_visible is False


def test_desktop_shell_app_can_refresh_service_status_explicitly():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(payload={"status": "ok"})
    app = DesktopShellApp(settings=settings, status_client=status_client)

    snapshot = app.check_service_status()

    assert snapshot["status"] == "ok"
    assert snapshot["last_checked"] is not None
    assert snapshot["error_hint"] is None
    assert snapshot["label"] == "Service status: available"


def test_service_unavailable_notification_is_not_repeated_without_state_change():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(should_fail=True)
    notifications = NotificationBridgeSkeleton()
    app = DesktopShellApp(
        settings=settings,
        status_client=status_client,
        notifications=notifications,
    )

    app.bootstrap()
    app.check_service_status()

    assert len(notifications.events) == 1
    assert notifications.events[0].body == "Cannot reach TraceFold API."


def test_notification_action_can_open_workbench():
    settings = DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )
    status_client = FakeStatusClient(payload={"status": "ok"})
    app = DesktopShellApp(settings=settings, status_client=status_client)

    result = app.handle_notification_action("open_workbench")

    assert result["status"] == "ready"
    assert result["url"] == "http://localhost:3000/workbench"
    assert app.state.window_visible is True
