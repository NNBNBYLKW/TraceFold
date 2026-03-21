from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


def _read(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8")


def test_tray_model_doc_keeps_tray_role_shell_only() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK2_TRAY_MODEL.md")

    assert "shell presence indicator" in content
    assert "minimal shell action entry" in content
    assert "not a business navigation tree" in content
    assert "not a second business interface" in content
    assert "last_menu_action" in content
    assert "last_shell_action" in content


def test_tray_acceptance_doc_keeps_tray_runtime_claims_small() -> None:
    content = _read("docs/POST_PHASE1_DESKTOP_R2_TASK2_ACCEPTANCE.md")

    assert "no business menu tree was added" in content
    assert "no Desktop-owned business logic was added" in content
    assert "not claim that Desktop now has a fully hardened OS-native tray runtime" in content
