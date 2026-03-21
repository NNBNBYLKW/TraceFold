from apps.desktop.app import main as desktop_main
import apps.desktop.__main__ as desktop_module_main


class FakeDesktopApp:
    def __init__(self, *, should_interrupt: bool = False) -> None:
        self.should_interrupt = should_interrupt
        self.calls: list[str] = []

    def start_runtime(self):
        self.calls.append("start_runtime")
        return {
            "startup_mode": "window",
            "workbench_url": "http://localhost:3000/workbench",
            "workbench_status": "ready",
            "workbench_error": None,
            "service_status_label": "Service status: available",
            "service_error_hint": None,
        }

    def wait_for_shutdown(self):
        self.calls.append("wait_for_shutdown")
        if self.should_interrupt:
            raise KeyboardInterrupt

    def quit(self):
        self.calls.append("quit")

    def close(self):
        self.calls.append("close")


def test_main_uses_runtime_startup_path(monkeypatch, capsys):
    fake_app = FakeDesktopApp()
    monkeypatch.setattr(desktop_main, "create_app", lambda: fake_app)

    desktop_main.main()

    output = capsys.readouterr().out
    assert fake_app.calls == ["start_runtime", "wait_for_shutdown", "close"]
    assert "TraceFold Desktop shell is running." in output
    assert "Workbench state: ready" in output
    assert "Service status: available" in output
    assert "Daily entry: shared workbench is open." in output
    assert "Recommended launch: python -m apps.desktop" in output


def test_main_quits_cleanly_on_keyboard_interrupt(monkeypatch, capsys):
    fake_app = FakeDesktopApp(should_interrupt=True)
    monkeypatch.setattr(desktop_main, "create_app", lambda: fake_app)

    desktop_main.main()

    output = capsys.readouterr().out
    assert fake_app.calls == ["start_runtime", "wait_for_shutdown", "quit", "close"]
    assert "Press Ctrl+C to quit." in output


def test_main_prints_recovery_hints_for_shell_and_service_failures(monkeypatch, capsys):
    class FailingDesktopApp(FakeDesktopApp):
        def start_runtime(self):
            self.calls.append("start_runtime")
            return {
                "startup_mode": "window",
                "workbench_url": "http://localhost:3000/workbench",
                "workbench_status": "error",
                "workbench_error": "Workbench URL could not be opened.",
                "service_status": "unavailable",
                "service_status_label": "Service status: unavailable",
                "service_error_hint": "Cannot reach TraceFold API. Check /api/healthz and TRACEFOLD_DESKTOP_API_BASE_URL.",
            }

    fake_app = FailingDesktopApp()
    monkeypatch.setattr(desktop_main, "create_app", lambda: fake_app)

    desktop_main.main()

    output = capsys.readouterr().out
    assert "Workbench detail: Workbench URL could not be opened." in output
    assert "Recovery: check TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL and confirm the Web dev server is running." in output
    assert "Service detail: Cannot reach TraceFold API. Check /api/healthz and TRACEFOLD_DESKTOP_API_BASE_URL." in output
    assert "Formal records are unaffected by this shell-side status failure." in output
    assert "Daily entry: shell is running, but the shared workbench did not open." in output


def test_main_prints_tray_mode_daily_entry_hint(monkeypatch, capsys):
    class TrayModeDesktopApp(FakeDesktopApp):
        def start_runtime(self):
            self.calls.append("start_runtime")
            return {
                "startup_mode": "tray",
                "workbench_url": "http://localhost:3000/workbench",
                "workbench_status": "idle",
                "workbench_error": None,
                "service_status": "ok",
                "service_status_label": "Service status: available",
                "service_error_hint": None,
            }

    fake_app = TrayModeDesktopApp()
    monkeypatch.setattr(desktop_main, "create_app", lambda: fake_app)

    desktop_main.main()

    output = capsys.readouterr().out
    assert "Daily entry: shell is resident in tray mode. Use tray Open TraceFold to open the shared workbench." in output


def test_repo_root_module_entry_delegates_to_desktop_main(monkeypatch):
    calls: list[str] = []
    monkeypatch.setattr(desktop_module_main, "main", lambda: calls.append("main"))

    desktop_module_main.run()

    assert calls == ["main"]
