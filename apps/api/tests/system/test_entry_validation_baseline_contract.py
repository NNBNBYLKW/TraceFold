from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]


def _read(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8")


def test_task4_validation_baseline_separates_validation_roles() -> None:
    content = _read("docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md")

    assert "## Daily Entry Baseline Validation" in content
    assert "## Broader System Smoke" in content
    assert "## Build Validation" in content
    assert "## Support And Setup Scripts" in content
    assert "not part of the daily A3 minimum validation set" in content
    assert "not a formal migration framework" in content
    assert "not the daily A3 entry baseline" in content


def test_task4_validation_baseline_lists_minimum_commands() -> None:
    content = _read("docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md")

    assert "python -m pytest -q apps\\desktop\\tests" in content
    assert (
        "python -m pytest -q apps\\api\\tests\\system\\test_entry_startup_baseline_contract.py"
        in content
    )
    assert (
        "python -m pytest -q apps\\api\\tests\\system\\test_entry_failure_signal_contract.py "
        "apps\\web\\tests\\test_semantics_contract.py "
        "apps\\web\\tests\\test_workbench_home_contract.py "
        "apps\\web\\tests\\test_health_ai_ui_contract.py "
        "apps\\web\\tests\\test_knowledge_ai_ui_contract.py"
    ) in content
    assert "python apps\\api\\scripts\\step9_ch1_acceptance_smoke.py" in content
    assert "npm run build" in content


def test_task4_validation_notes_point_back_to_task1_to_task3_docs() -> None:
    content = _read("docs/POST_PHASE1_A3_TASK4_VALIDATION_NOTES.md")

    assert "docs/POST_PHASE1_A3_TASK1_ACCEPTANCE.md" in content
    assert "docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md" in content
    assert "docs/POST_PHASE1_A3_TASK3_FAILURE_SIGNALS.md" in content
    assert "docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md" in content
    assert "daily entry baseline validation" in content
    assert "broader system smoke" in content
    assert "build validation" in content


def test_task4_scope_keeps_task4_small_and_entry_only() -> None:
    content = _read("docs/POST_PHASE1_A3_TASK4_SCOPE.md")

    assert "Desktop shell runtime validation" in content
    assert "startup and config baseline validation" in content
    assert "failure signal and recovery hint validation" in content
    assert "change Telegram" in content
    assert "expand Desktop beyond shell-only" in content
    assert "service layer remains the only business logic center" in content
