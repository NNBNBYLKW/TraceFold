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
    assert "Rule-based alerts are reminders derived from formal health records." in MAIN_TS
    assert "AI derivations are generated from the formal record." in MAIN_TS
    assert "They do not replace formal facts or rule alerts." in MAIN_TS
    assert "AI-derived Summary" in MAIN_TS
    assert "AI center" not in MAIN_TS


def test_current_mode_copy_keeps_workbench_role_thin() -> None:
    assert "Current mode only changes entry context." in MAIN_TS
    assert "It does not change formal record semantics." in MAIN_TS


def test_web_failure_wording_distinguishes_unavailable_and_invalid_response() -> None:
    api_ts = Path("apps/web/src/api.ts").read_text(encoding="utf-8")

    assert "TraceFold API is unavailable. Check /api/healthz and VITE_API_BASE_URL." in api_ts
    assert "TraceFold API returned an invalid response. Check the API process and try again." in api_ts


def test_ai_derivation_empty_state_says_not_generated() -> None:
    assert "AI-derived summary is not available yet." in MAIN_TS
    assert "The formal content remains available." in MAIN_TS
    assert "It is only available for subjective health records. The formal record remains available." in MAIN_TS


def test_failure_states_include_recovery_hints_and_formal_fact_safety_note() -> None:
    assert "Check /api/healthz first. If the API is healthy, confirm the API base URL in .env." in MAIN_TS
    assert "This entry-side failure does not change existing formal records." in MAIN_TS
    assert "This response failure does not change existing formal records." in MAIN_TS
    assert "AI derivation failed. The formal record remains available." in MAIN_TS


def test_knowledge_detail_copy_keeps_formal_and_ai_boundaries_clear() -> None:
    assert "Formal content remains the record of truth for this knowledge entry." in MAIN_TS
    assert "AI-derived summary is generated from the formal record. It does not replace the formal content." in MAIN_TS
    assert "AI-derived summary generation failed. The formal content remains available." in MAIN_TS
    assert "AI-derived summary is invalidated and should be recomputed before relying on it." in MAIN_TS
    assert "AI-derived summary generation failed. The formal record remains available." in MAIN_TS


def test_workbench_and_dashboard_distinguish_loading_empty_unavailable_and_degraded() -> None:
    assert "Loading state is shown while the current route is still fetching its shared API inputs." in MAIN_TS
    assert "Empty means the shared API responded successfully, but this page does not have records or summary data yet." in MAIN_TS
    assert "Unavailable means the shared API route could not be reached or returned an unusable response." in MAIN_TS
    assert "Degraded means /api/system/status reported a shared runtime warning even though reads may still succeed." in MAIN_TS
