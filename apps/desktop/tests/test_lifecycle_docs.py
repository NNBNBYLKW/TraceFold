from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


def _read(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8")


def test_lifecycle_model_doc_captures_supported_shell_state_fields() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK1_LIFECYCLE_MODEL.md")

    assert "runtime_started" in content
    assert "resident" in content
    assert "window_visible" in content
    assert "tray_visible" in content
    assert "service_status" in content
    assert "workbench_state" in content
    assert "`window` mode" in content
    assert "`tray` mode" in content


def test_lifecycle_acceptance_doc_keeps_shell_only_boundary_explicit() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK1_ACCEPTANCE.md")

    assert "Desktop still does not own business pages" in content
    assert "Desktop still does not own business logic" in content
    assert "Desktop still does not own write paths" in content
    assert "not a fully hardened native runtime" in content
