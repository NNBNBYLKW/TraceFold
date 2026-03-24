from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
STYLE_CSS = Path("apps/web/src/style.css").read_text(encoding="utf-8")


def test_shared_state_panels_cover_loading_empty_unavailable_and_degraded() -> None:
    assert "function renderLoadingState()" in MAIN_TS
    assert "function renderEmptyState(" in MAIN_TS
    assert "function renderUnavailableState(" in MAIN_TS
    assert "function renderDegradedState(" in MAIN_TS
    assert "tone: 'loading'" in MAIN_TS
    assert "tone: 'empty'" in MAIN_TS
    assert "tone: 'unavailable'" in MAIN_TS
    assert "tone: 'degraded'" in MAIN_TS


def test_shared_state_copy_is_reused_across_workbench_knowledge_and_health() -> None:
    assert "Loading state is shown while the current route is still fetching its shared API inputs." in MAIN_TS
    assert "Unavailable means the shared API route could not be reached or returned an unusable response." in MAIN_TS
    assert "Empty means the shared API responded successfully, but this page does not have records or summary data yet." in MAIN_TS
    assert "Health alerts are currently empty." in MAIN_TS
    assert "AI-derived summary is unavailable right now." in MAIN_TS


def test_status_panel_styles_keep_ready_warning_error_and_empty_tones_aligned() -> None:
    assert ".status-panel.is-ready" in STYLE_CSS
    assert ".status-panel.is-warning" in STYLE_CSS
    assert ".status-panel.is-error" in STYLE_CSS
    assert ".status-panel.is-empty" in STYLE_CSS
    assert ".status-eyebrow" in STYLE_CSS
    assert ".status-title" in STYLE_CSS
