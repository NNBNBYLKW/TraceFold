from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


def _read(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8")


def test_repo_root_launch_entry_exists() -> None:
    module_entry = ROOT / "apps" / "desktop" / "__main__.py"

    assert module_entry.exists()
    assert "from apps.desktop.app.main import main" in module_entry.read_text(encoding="utf-8")


def test_desktop_env_example_mentions_daily_launch_command() -> None:
    content = _read("apps/desktop/.env.example")

    assert "Recommended daily shell runtime path today: window" in content


def test_task2_startup_baseline_can_be_updated_to_shorter_repo_root_entry() -> None:
    content = _read("docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md")

    assert "python -m apps.desktop" in content
    assert "python -m apps.desktop.app.main" in content


def test_launch_model_doc_separates_recommended_and_lower_level_entries() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK3_LAUNCH_MODEL.md")

    assert "Recommended repo-root launch command" in content
    assert "python -m apps.desktop" in content
    assert "Lower-level compatibility path" in content
    assert "python -m apps.desktop.app.main" in content


def test_launch_acceptance_doc_keeps_launch_hardening_small() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK3_ACCEPTANCE.md")

    assert "no installer or packaging layer was added" in content
    assert "no Desktop-owned business UI was added" in content
    assert "no Desktop-owned business logic was added" in content
    assert "not a packaged desktop distribution" in content
