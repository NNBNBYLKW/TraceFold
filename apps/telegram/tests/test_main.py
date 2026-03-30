from apps.telegram.app import main as telegram_main


class FakeApp:
    def __init__(self, *, interrupt: bool = False) -> None:
        self.interrupt = interrupt
        self.calls: list[str] = []

    def probe_dependencies(self) -> dict[str, object]:
        self.calls.append("probe")
        return {"ok": True}

    def run_polling(self) -> None:
        self.calls.append("run_polling")
        if self.interrupt:
            raise KeyboardInterrupt()

    def close(self) -> None:
        self.calls.append("close")


def test_main_runs_probe_then_polling_and_closes(monkeypatch) -> None:
    fake_app = FakeApp()
    monkeypatch.setattr(telegram_main, "create_app", lambda: fake_app)

    telegram_main.main()

    assert fake_app.calls == ["probe", "run_polling", "close"]


def test_main_closes_cleanly_on_keyboard_interrupt(monkeypatch) -> None:
    fake_app = FakeApp(interrupt=True)
    monkeypatch.setattr(telegram_main, "create_app", lambda: fake_app)

    telegram_main.main()

    assert fake_app.calls == ["probe", "run_polling", "close"]
