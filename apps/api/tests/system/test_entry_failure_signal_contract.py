from pathlib import Path


WEB_API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")
WEB_MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
DESKTOP_APP_TS = Path("apps/desktop/app/shell/app.py").read_text(encoding="utf-8")
DESKTOP_MAIN_TS = Path("apps/desktop/app/main.py").read_text(encoding="utf-8")
TASK3_SIGNALS = Path("docs/POST_PHASE1_A3_TASK3_FAILURE_SIGNALS.md").read_text(encoding="utf-8")
TASK3_HINTS = Path("docs/POST_PHASE1_A3_TASK3_RECOVERY_HINTS.md").read_text(encoding="utf-8")


def test_web_failure_wording_distinguishes_api_unavailable_and_invalid_response() -> None:
    assert "TraceFold API is unavailable. Check /api/healthz and VITE_API_BASE_URL." in WEB_API_TS
    assert "TraceFold API returned an invalid response. Check the API process and try again." in WEB_API_TS


def test_web_failure_signals_keep_formal_fact_safety_visible() -> None:
    assert "This entry-side failure does not change existing formal records." in WEB_MAIN_TS
    assert "This response failure does not change existing formal records." in WEB_MAIN_TS
    assert "AI derivation failed. The formal record remains available." in WEB_MAIN_TS
    assert "AI derivation has not been generated for this knowledge record yet. The formal record remains available." in WEB_MAIN_TS


def test_desktop_failure_signals_distinguish_api_and_workbench_failures() -> None:
    assert "TraceFold API returned an invalid response. Check /api/healthz and the API process." in DESKTOP_APP_TS
    assert "Cannot reach TraceFold API. Check /api/healthz and TRACEFOLD_DESKTOP_API_BASE_URL." in DESKTOP_APP_TS
    assert "Recovery: check TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL and confirm the Web dev server is running." in DESKTOP_MAIN_TS
    assert "Formal records are unaffected by this shell-side status failure." in DESKTOP_MAIN_TS


def test_task3_docs_match_runtime_failure_signal_split() -> None:
    assert "API unavailable" in TASK3_SIGNALS
    assert "API invalid response" in TASK3_SIGNALS
    assert "Workbench URL could not be opened" in TASK3_SIGNALS
    assert "These shell-side failures do not imply formal records are damaged." in TASK3_HINTS
