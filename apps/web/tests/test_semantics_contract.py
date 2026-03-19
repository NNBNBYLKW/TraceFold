from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")


def test_detail_pages_use_consistent_record_and_pending_titles() -> None:
    assert "Pending Item" in MAIN_TS
    assert "Expense Record" in MAIN_TS
    assert "Knowledge Record" in MAIN_TS
    assert "Health Record" in MAIN_TS
    assert "Pending Detail" not in MAIN_TS
    assert "Expense Detail" not in MAIN_TS
    assert "Knowledge Detail" not in MAIN_TS
    assert "Health Detail" not in MAIN_TS


def test_source_reference_labels_are_human_readable() -> None:
    assert "Source Reference" in MAIN_TS
    assert "Source Capture ID" in MAIN_TS
    assert "Source Pending ID" in MAIN_TS
    assert "Source Text" in MAIN_TS
    assert "${renderField('source_capture_id'" not in MAIN_TS
    assert "${renderField('source_pending_id'" not in MAIN_TS


def test_rule_alerts_and_ai_derivations_keep_distinct_layer_language() -> None:
    assert "Rule alerts are reminders derived from formal records." in MAIN_TS
    assert "AI derivations are generated from the formal record." in MAIN_TS
    assert "They do not replace formal facts or rule alerts." in MAIN_TS
    assert "AI Derivation" in MAIN_TS
    assert "AI Summary" not in MAIN_TS


def test_current_mode_copy_keeps_workbench_role_thin() -> None:
    assert "Current mode only changes entry context." in MAIN_TS
    assert "It does not change formal record semantics." in MAIN_TS


def test_web_failure_wording_distinguishes_unavailable_and_invalid_response() -> None:
    api_ts = Path("apps/web/src/api.ts").read_text(encoding="utf-8")

    assert "TraceFold service is unavailable right now." in api_ts
    assert "TraceFold returned an invalid response." in api_ts


def test_ai_derivation_empty_state_says_not_generated() -> None:
    assert "AI derivation has not been generated for this knowledge record yet." in MAIN_TS
    assert "AI derivation has not been generated for this health record yet." in MAIN_TS
