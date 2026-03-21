from __future__ import annotations

"""Minimal Step 9 Chapter 1 acceptance runner.

This script reuses the current repo test and build commands. It is a repeatable
smoke runner, not a new test framework, not a daily startup entry, and not a
full external-runtime E2E rig.
"""

import subprocess
import sys
from pathlib import Path
from typing import NamedTuple


ROOT = Path(__file__).resolve().parents[3]
WEB_DIR = ROOT / "apps" / "web"


class AcceptanceStage(NamedTuple):
    name: str
    command: list[str]
    cwd: Path


COMMANDS: list[AcceptanceStage] = [
    AcceptanceStage(
        "system-smoke",
        [
            sys.executable,
            "-m",
            "pytest",
            "-q",
            "apps/api/tests/system/test_step9_ch1_system_wide_acceptance.py",
        ],
        ROOT,
    ),
    AcceptanceStage(
        "workbench-backend",
        [
            sys.executable,
            "-m",
            "pytest",
            "-q",
            "apps/api/tests/domains/workbench",
        ],
        ROOT,
    ),
    AcceptanceStage(
        "desktop-regression",
        [
            sys.executable,
            "-m",
            "pytest",
            "-q",
            "apps/desktop/tests",
        ],
        ROOT,
    ),
    AcceptanceStage(
        "telegram-regression",
        [
            sys.executable,
            "-m",
            "pytest",
            "-q",
            "apps/telegram/tests/test_telegram_final_consistency.py",
            "apps/telegram/tests/test_summary_handler.py",
            "apps/telegram/tests/test_pending_handler.py",
            "apps/telegram/tests/test_capture_handler.py",
            "apps/telegram/tests/test_dispatch.py",
            "apps/telegram/tests/test_app.py",
            "apps/telegram/tests/test_config.py",
            "apps/telegram/tests/test_observability.py",
            "apps/telegram/tests/test_formatting.py",
            "apps/telegram/tests/test_api_client.py",
        ],
        ROOT,
    ),
    AcceptanceStage(
        "web-build",
        ["npm.cmd", "run", "build"],
        WEB_DIR,
    ),
]


def main() -> int:
    for stage in COMMANDS:
        print(f"[STEP9_CH1] stage={stage.name} running: {' '.join(stage.command)}", flush=True)
        try:
            completed = subprocess.run(stage.command, cwd=stage.cwd)
        except OSError as exc:
            print(f"[STEP9_CH1] stage={stage.name} failed to start: {exc}", flush=True)
            return 1
        if completed.returncode != 0:
            print(
                f"[STEP9_CH1] stage={stage.name} failed with exit code {completed.returncode}.",
                flush=True,
            )
            return completed.returncode
        print(f"[STEP9_CH1] stage={stage.name} passed.", flush=True)
    print("[STEP9_CH1] acceptance smoke completed successfully.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
