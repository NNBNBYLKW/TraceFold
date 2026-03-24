from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_workbench_home_rendering_keeps_five_sections_in_frozen_order() -> None:
    positions = [
        MAIN_TS.index("1. Current Mode"),
        MAIN_TS.index("2. Templates"),
        MAIN_TS.index("3. Fixed Shortcuts"),
        MAIN_TS.index("4. Recent Context"),
        MAIN_TS.index("5. Dashboard Summary"),
    ]

    assert positions == sorted(positions)
    assert "kind: 'workbench'" in MAIN_TS
    assert "return { kind: 'redirect', to: '/workbench' }" in MAIN_TS


def test_workbench_template_ui_flow_uses_shared_api_contract() -> None:
    assert "fetchWorkbenchHome" in API_TS
    assert "createWorkbenchTemplate" in API_TS
    assert "updateWorkbenchTemplate" in API_TS
    assert "applyWorkbenchTemplate" in API_TS
    assert "data-workbench-action=\"template-apply\"" in MAIN_TS
    assert "data-workbench-form=\"true\" data-workbench-kind=\"template\"" in MAIN_TS
    assert "localStorage" not in MAIN_TS


def test_workbench_shortcut_ui_flow_uses_shared_api_contract() -> None:
    assert "fetchWorkbenchShortcuts" in API_TS
    assert "createWorkbenchShortcut" in API_TS
    assert "updateWorkbenchShortcut" in API_TS
    assert "deleteWorkbenchShortcut" in API_TS
    assert "data-workbench-form=\"true\" data-workbench-kind=\"shortcut\"" in MAIN_TS
    assert "Shortcut targets stay limited to route or module-view context." in MAIN_TS


def test_recent_restore_flow_uses_route_snapshot_and_keeps_resume_semantics() -> None:
    assert "Resume" in MAIN_TS
    assert "recent.route_snapshot" in MAIN_TS
    assert "Recent is for continuing work. It is not a history log or audit timeline." in MAIN_TS


def test_dashboard_summary_is_present_but_not_dominant() -> None:
    assert "Summary stays summary. The workbench homepage does not replace the full dashboard." in MAIN_TS
    recent_position = MAIN_TS.index("4. Recent Context")
    dashboard_position = MAIN_TS.index("5. Dashboard Summary")
    assert dashboard_position > recent_position


def test_workbench_formally_consumes_home_dashboard_and_runtime_status() -> None:
    assert "fetchWorkbenchHome()" in MAIN_TS
    assert "fetchDashboard()" in MAIN_TS
    assert "fetchRuntimeStatus()" in MAIN_TS
    assert "System status is ready." in MAIN_TS
    assert "Workbench summary inputs are partially unavailable." in MAIN_TS
