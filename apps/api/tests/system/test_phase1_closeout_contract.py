from __future__ import annotations

from pathlib import Path


CH6_DOD = Path("docs/STEP9_CH6_PHASE1_DEFINITION_OF_DONE.md").read_text(encoding="utf-8")
CH6_COMPLETED = Path("docs/STEP9_CH6_PHASE1_COMPLETED_CAPABILITIES.md").read_text(encoding="utf-8")
CH6_GAPS = Path("docs/STEP9_CH6_PHASE1_ACCEPTABLE_GAPS_AND_TECH_DEBT.md").read_text(encoding="utf-8")
CH6_BOUNDARIES = Path("docs/STEP9_CH6_PHASE1_LOCKED_BOUNDARIES.md").read_text(encoding="utf-8")
CH6_BASELINE = Path("docs/STEP9_CH6_POST_PHASE1_BASELINE.md").read_text(encoding="utf-8")


def test_phase1_definition_of_done_locks_unified_mainline_and_entry_boundaries() -> None:
    assert "Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation" in CH6_DOD
    assert "service layer remains the only real business logic center" in CH6_DOD
    assert "Web, Desktop, and Telegram all remain entry surfaces" in CH6_DOD


def test_phase1_completed_capabilities_do_not_overstate_scope() -> None:
    assert "Telegram Lightweight Entry" in CH6_COMPLETED
    assert "Desktop Shell Boundary" in CH6_COMPLETED
    assert "Important Note" in CH6_COMPLETED
    assert "not “maximally expanded”" in CH6_COMPLETED


def test_phase1_gaps_document_keeps_skeletons_and_missing_frameworks_as_debt() -> None:
    assert "Desktop shell runtime is still skeleton-level" in CH6_GAPS
    assert "Repo-style ensure scripts are not a migration framework" in CH6_GAPS
    assert "These are not Phase 1 gaps to “finish later inside the same phase”" in CH6_GAPS


def test_phase1_locked_boundaries_and_post_phase1_baseline_are_explicit() -> None:
    assert "AI does not confirm, discard, force-insert, or mutate formal facts." in CH6_BOUNDARIES
    assert "Desktop remains shell-only." in CH6_BOUNDARIES
    assert "Telegram remains a lightweight adapter." in CH6_BOUNDARIES
    assert "Required Reading Order For New Work" in CH6_BASELINE
    assert "Any new task after Phase 1 must explicitly answer" in CH6_BASELINE
