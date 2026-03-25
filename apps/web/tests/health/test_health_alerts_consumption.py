from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_health_pages_consume_formal_health_reads_and_formal_alert_lifecycle_api() -> None:
    assert "fetchHealthList(buildHealthApiParams(query))" in MAIN_TS
    assert "fetchHealthDetail(id)" in MAIN_TS
    assert "fetchAlertList({ domain: 'health' })" in MAIN_TS
    assert "fetchAlertList({ domain: 'health', source_record_id: id })" in MAIN_TS
    assert "export async function acknowledgeAlert(" in API_TS
    assert "export async function resolveAlert(" in API_TS


def test_health_alert_presentation_distinguishes_open_acknowledged_resolved_empty_and_unavailable() -> None:
    assert "Open Alerts" in MAIN_TS
    assert "Acknowledged Alerts" in MAIN_TS
    assert "Resolved Alerts" in MAIN_TS
    assert "Health records are unavailable." in MAIN_TS
    assert "Health alerts are unavailable right now." in MAIN_TS
    assert "No rule alerts for health records." in MAIN_TS
    assert "Resolved means the reminder was handled. It does not mean the health fact was automatically corrected." in MAIN_TS


def test_health_alert_actions_stay_minimal_and_do_not_form_an_alert_center() -> None:
    assert "Acknowledge Alert" in MAIN_TS
    assert "Resolve Alert" in MAIN_TS
    assert "section-action-row alert-actions" in MAIN_TS
    assert "Rule-based alerts are reminders derived from formal health records." in MAIN_TS
    assert "Formal health records remain the primary read layer for this page." in MAIN_TS


def test_health_pages_do_not_show_ai_section_or_ai_wording() -> None:
    assert "renderHealthAiSummarySection(" not in MAIN_TS
    assert "AI Derivation" not in MAIN_TS
    assert "Supportive interpretation" not in MAIN_TS
