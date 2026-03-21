from apps.desktop.app.core.config import DesktopShellSettings
from apps.desktop.app.shell.app import DesktopShellApp
from apps.desktop.app.shell.window import MainWindowSkeleton


class FakeStatusClient:
    def __init__(self) -> None:
        self.closed = False

    def get_status(self):
        return {"status": "ok"}

    def get_workbench_home(self):
        return {"current_mode": {"template_name": "Home"}}

    def close(self):
        self.closed = True


def _settings(startup_mode: str = "window") -> DesktopShellSettings:
    return DesktopShellSettings.model_validate(
        {
            "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL": "http://localhost:3000",
            "TRACEFOLD_DESKTOP_API_BASE_URL": "http://localhost:8000/api",
            "TRACEFOLD_DESKTOP_STARTUP_MODE": startup_mode,
            "TRACEFOLD_DESKTOP_DEBUG": False,
            "TRACEFOLD_DESKTOP_LOG_ENABLED": True,
        }
    )


def _window(open_counter: list[str] | None = None) -> MainWindowSkeleton:
    launches = open_counter if open_counter is not None else []
    return MainWindowSkeleton(
        title="TraceFold Desktop Shell",
        workbench_url="http://localhost:3000",
        url_launcher=lambda url: launches.append(url) or True,
    )


def test_bootstrap_marks_shell_resident_without_marking_runtime_started() -> None:
    app = DesktopShellApp(
        settings=_settings("window"),
        status_client=FakeStatusClient(),
        window=_window(),
    )

    bootstrap = app.bootstrap()

    assert bootstrap["tray_visible"] is True
    assert bootstrap["resident"] is True
    assert app.state.runtime_started is False
    assert app.state.resident is True
    assert app.state.tray_visible is True
    assert app.state.window_visible is False


def test_start_runtime_is_idempotent_while_shell_is_running() -> None:
    launches: list[str] = []
    app = DesktopShellApp(
        settings=_settings("window"),
        status_client=FakeStatusClient(),
        window=_window(launches),
    )

    first = app.start_runtime()
    second = app.start_runtime()

    assert first["runtime_started"] is True
    assert second["runtime_started"] is True
    assert first["resident"] is True
    assert second["resident"] is True
    assert launches == ["http://localhost:3000/workbench"]


def test_tray_toggle_keeps_runtime_started_and_resident_while_switching_visibility() -> None:
    app = DesktopShellApp(
        settings=_settings("tray"),
        status_client=FakeStatusClient(),
        window=_window(),
    )
    app.start_runtime()

    shown = app.handle_tray_action("toggle_window")
    hidden = app.handle_tray_action("toggle_window")

    assert shown["menu_action"] == "toggle_window"
    assert shown["shell_action"] == "open_workbench"
    assert shown["window_visible"] is True
    assert app.state.runtime_started is True
    assert app.state.resident is True
    assert hidden["menu_action"] == "toggle_window"
    assert hidden["shell_action"] == "hide_window"
    assert hidden["window_visible"] is False
    assert hidden["resident"] is True
    assert app.state.runtime_started is True
    assert app.state.resident is True


def test_close_clears_runtime_presence_without_forcing_quit_requested() -> None:
    status_client = FakeStatusClient()
    app = DesktopShellApp(
        settings=_settings("window"),
        status_client=status_client,
        window=_window(),
    )
    app.start_runtime()

    app.close()

    assert app.state.runtime_started is False
    assert app.state.resident is False
    assert app.state.window_visible is False
    assert app.state.tray_visible is False
    assert app.state.quit_requested is False
    assert status_client.closed is True
