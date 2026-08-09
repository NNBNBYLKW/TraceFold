from __future__ import annotations

from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
STEP8_ARCHIVE = REPO_ROOT / "docs/archive/phase1/step8"
STEP9_ARCHIVE = REPO_ROOT / "docs/archive/phase1/step9"
STEP8_SCOPE = (STEP8_ARCHIVE / "STEP8_SCOPE.md").read_text(encoding="utf-8")
STEP8_DRIFT = (STEP8_ARCHIVE / "STEP8_DRIFT_WARNINGS.md").read_text(encoding="utf-8")
CH5_REVIEW = (STEP9_ARCHIVE / "STEP9_CH5_PHASE1_NON_GOALS_REVIEW.md").read_text(
    encoding="utf-8"
)
DESKTOP_ENV = (REPO_ROOT / "apps/desktop/.env.example").read_text(encoding="utf-8")
TELEGRAM_ENV = (REPO_ROOT / "apps/telegram/.env.example").read_text(encoding="utf-8")


def test_step8_scope_relocks_key_non_goals() -> None:
    assert "generic workflow or script engines" in STEP8_SCOPE
    assert "desktop-heavy business client" in STEP8_SCOPE
    assert "Telegram workbench or management-client mode" in STEP8_SCOPE
    assert "knowledge-graph-centered product restructuring" in STEP8_SCOPE
    assert "AI write-back into formal fact tables" in STEP8_SCOPE


def test_step8_drift_warnings_cover_phase1_high_risk_directions() -> None:
    assert "## AI Drift" in STEP8_DRIFT
    assert "## Knowledge Drift" in STEP8_DRIFT
    assert "## Health Drift" in STEP8_DRIFT
    assert "## External Tool Drift" in STEP8_DRIFT
    assert "## Engineering Scale Drift" in STEP8_DRIFT


def test_env_examples_keep_desktop_and_telegram_out_of_management_roles() -> None:
    assert "does not imply a native business client" in DESKTOP_ENV
    assert "does not imply template CRUD, workbench modes, or a management-client role" in TELEGRAM_ENV


def test_phase1_non_goals_review_explicitly_locks_ai_desktop_and_telegram_boundaries() -> None:
    assert "AI remains derivation-only" in CH5_REVIEW
    assert "Desktop remains shell-only in Phase 1." in CH5_REVIEW
    assert "Telegram remains a lightweight adapter and not a management client." in CH5_REVIEW
