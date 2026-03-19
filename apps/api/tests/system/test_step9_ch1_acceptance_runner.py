from __future__ import annotations

import importlib.util
from pathlib import Path


SCRIPT_PATH = Path("apps/api/scripts/step9_ch1_acceptance_smoke.py")


def _load_module():
    spec = importlib.util.spec_from_file_location("step9_ch1_acceptance_smoke", SCRIPT_PATH)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_acceptance_runner_prints_stage_failure_details(monkeypatch, capsys) -> None:
    module = _load_module()

    class Result:
        returncode = 2

    def fake_run(command, cwd):
        return Result()

    monkeypatch.setattr(module.subprocess, "run", fake_run)

    exit_code = module.main()
    output = capsys.readouterr().out

    assert exit_code == 2
    assert "stage=system-smoke running" in output
    assert "stage=system-smoke failed with exit code 2." in output


def test_acceptance_runner_reports_startup_failure(monkeypatch, capsys) -> None:
    module = _load_module()

    def fake_run(command, cwd):
        raise OSError("cannot start")

    monkeypatch.setattr(module.subprocess, "run", fake_run)

    exit_code = module.main()
    output = capsys.readouterr().out

    assert exit_code == 1
    assert "failed to start" in output
