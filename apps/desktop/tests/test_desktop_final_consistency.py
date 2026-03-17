from apps.desktop.app.clients.status_client import DesktopStatusClientError
from apps.desktop.app.core.config import DesktopShellSettings
from apps.desktop.app.shell.app import DesktopShellApp
from apps.desktop.app.shell.notifications import NotificationBridgeSkeleton


class RecordingStatusClient:
    def __init__(self, *, should_fail: bool = False) -> None:
        self.should_fail = should_fail
        self.calls = 0
        self.closed = False

    def get_status(self):
        self.calls += 1
        if self.should_fail:
            raise DesktopStatusClientError("TraceFold API is unavailable.")
        return {"status": "ok"}

    def close(self):
        self.closed = True


def _settings() -> DesktopShellSettings:
    return DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": "window",
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )


def test_desktop_final_consistency_keeps_shell_only_paths():
    status_client = RecordingStatusClient(should_fail=True)
    notifications = NotificationBridgeSkeleton()
    app = DesktopShellApp(
        settings=_settings(),
        status_client=status_client,
        notifications=notifications,
    )

    bootstrap = app.bootstrap()
    reopened = app.handle_notification_action("open_workbench")
    manual_status = app.check_service_status()
    menu = app.tray_menu_items()
    app.close()

    assert bootstrap["service_status"] == "unavailable"
    assert reopened["status"] == "ready"
    assert reopened["url"] == "http://localhost:3000"
    assert manual_status["status"] == "unavailable"
    assert status_client.calls == 2
    assert [item["label"] for item in menu] == [
        "Open TraceFold",
        "Hide Window",
        "Quit",
    ]
    assert len(notifications.events) == 1
    assert notifications.events[0].action_key == "open_workbench"
    assert not hasattr(app, "submit_capture")
    assert not hasattr(app, "confirm_pending")
    assert not hasattr(app, "force_insert")
    assert status_client.closed is True
