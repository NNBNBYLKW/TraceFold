from pathlib import Path


STARTUP_BASELINE = Path("docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md").read_text(encoding="utf-8")
RECOVERY_PATHS = Path("docs/POST_PHASE1_A3_TASK2_RECOVERY_PATHS.md").read_text(encoding="utf-8")
RUNNER = Path("apps/api/scripts/step9_ch1_acceptance_smoke.py").read_text(encoding="utf-8")
API_ENV = Path("apps/api/.env.example").read_text(encoding="utf-8")
WEB_ENV = Path("apps/web/.env.example").read_text(encoding="utf-8")
DESKTOP_ENV = Path("apps/desktop/.env.example").read_text(encoding="utf-8")


def test_startup_baseline_freezes_daily_entry_commands() -> None:
    assert "uvicorn app.main:app --host 127.0.0.1 --port 8000" in STARTUP_BASELINE
    assert "npm run dev" in STARTUP_BASELINE
    assert "python -m apps.desktop.app.main" in STARTUP_BASELINE
    assert "1. Start the API" in STARTUP_BASELINE
    assert "2. Start the Web app" in STARTUP_BASELINE
    assert "3. Start the Desktop shell" in STARTUP_BASELINE


def test_startup_baseline_separates_daily_startup_from_validation_tools() -> None:
    assert "not the daily way to run the system" in STARTUP_BASELINE
    assert "not a daily startup entry" in RUNNER
    assert "migrate_step8_workbench.py" in STARTUP_BASELINE


def test_recovery_paths_distinguish_api_web_and_desktop_failures() -> None:
    assert "API Is Not Started" in RECOVERY_PATHS
    assert "Web Starts But Backend Is Unreachable" in RECOVERY_PATHS
    assert "Desktop Starts But Cannot Open Workbench" in RECOVERY_PATHS
    assert "Desktop Shows Service Unavailable" in RECOVERY_PATHS
    assert "Workbench URL failure and API status failure are related but not identical" in RECOVERY_PATHS


def test_env_examples_match_minimum_startup_story() -> None:
    assert "TRACEFOLD_API_HOST=127.0.0.1" in API_ENV
    assert "VITE_API_BASE_URL=http://127.0.0.1:8000" in WEB_ENV
    assert "TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL=http://127.0.0.1:3000/workbench" in DESKTOP_ENV
    assert "TRACEFOLD_DESKTOP_API_BASE_URL=http://127.0.0.1:8000/api" in DESKTOP_ENV
