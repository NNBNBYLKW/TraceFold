from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def _render_workbench_view_block() -> str:
    start = MAIN_TS.index("function renderWorkbenchView(")
    end = MAIN_TS.index("function renderWorkbenchFlash(")
    return MAIN_TS[start:end]


def test_workbench_home_rendering_keeps_five_sections_in_frozen_order() -> None:
    block = _render_workbench_view_block()
    positions = [
        block.index("renderWorkbenchCurrentModeSection("),
        block.index("renderWorkbenchTemplatesSection("),
        block.index("renderWorkbenchShortcutsSection("),
        block.index("renderWorkbenchDashboardSummarySection("),
        block.index("renderWorkbenchRecentSection("),
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
    assert "Recent helps you resume active work after you already know the next mode or page." in MAIN_TS


def test_dashboard_summary_is_present_but_not_dominant() -> None:
    block = _render_workbench_view_block()
    assert "Summary stays support. Use it after choosing a mode or entry path to confirm where pressure or recent movement exists." in MAIN_TS
    current_mode_position = block.index("renderWorkbenchCurrentModeSection(")
    templates_position = block.index("renderWorkbenchTemplatesSection(")
    shortcuts_position = block.index("renderWorkbenchShortcutsSection(")
    dashboard_position = block.index("renderWorkbenchDashboardSummarySection(")
    assert current_mode_position < templates_position < shortcuts_position < dashboard_position


def test_workbench_formally_consumes_home_dashboard_and_runtime_status() -> None:
    assert "fetchWorkbenchHome()" in MAIN_TS
    assert "fetchDashboard()" in MAIN_TS
    assert "fetchRuntimeStatus()" in MAIN_TS
    assert "fetchLocalOperability()" in MAIN_TS
    assert "System status is ready." in MAIN_TS
    assert "Workbench summary inputs are partially unavailable." in MAIN_TS


def test_templates_are_presented_as_entry_only_work_modes() -> None:
    assert "Templates are structured work-mode entry points." in MAIN_TS
    assert "they do not execute actions or automate workflows." in MAIN_TS
    assert "Built-in modes" in MAIN_TS
    assert "User modes" in MAIN_TS
    assert "Set current mode" in MAIN_TS
    assert "Set default entry" in MAIN_TS


def test_local_continuity_stays_support_level_and_sqlite_first() -> None:
    block = _render_workbench_view_block()
    assert "renderLocalOperabilitySection(" in block
    assert block.index("renderRuntimeStatusSection(") < block.index("renderLocalOperabilitySection(")
    assert "fetchLocalOperability" in API_TS
    assert "createLocalBackup" in API_TS
    assert "restoreLocalBackup" in API_TS
    assert "exportCaptureBundle" in API_TS
    assert "importCaptureBundle" in API_TS
    assert "SQLite remains the single source of truth" in MAIN_TS
    assert "this section stays support-level rather than becoming an admin console." in MAIN_TS
    assert "Import capture bundle" in MAIN_TS
    assert "Open capture records" in MAIN_TS
