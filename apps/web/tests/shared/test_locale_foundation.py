from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
LOCALE_TS = Path("apps/web/src/locale.ts").read_text(encoding="utf-8")


def test_locale_helper_detects_browser_language_and_persists_manual_choice() -> None:
    assert "type UiLocale = 'en' | 'zh'" in LOCALE_TS
    assert "tracefold.web.locale" in LOCALE_TS
    assert "navigator.languages" in LOCALE_TS
    assert "navigator.language" in LOCALE_TS
    assert "localStorage" in LOCALE_TS
    assert "persistLocale" in LOCALE_TS


def test_shared_shell_exposes_user_facing_locale_toggle() -> None:
    assert "function renderLocaleToggle()" in MAIN_TS
    assert 'data-locale-choice="en"' in MAIN_TS
    assert 'data-locale-choice="zh"' in MAIN_TS
    assert "setUiLocale(nextLocale)" in MAIN_TS
    assert "document.documentElement.lang" in MAIN_TS


def test_shared_copy_layer_covers_shell_state_and_local_continuity_foundation() -> None:
    assert "const SHARED_COPY =" in MAIN_TS
    assert "'Local-first workspace'" in MAIN_TS
    assert "'Loading shared page inputs.'" in MAIN_TS
    assert "'Unavailable means the shared API route could not be reached or returned an unusable response.'" in MAIN_TS
    assert "'Retry'" in MAIN_TS
    assert "'Restore local database'" in MAIN_TS
    assert "'Export capture bundle'" in MAIN_TS
    assert "'Import capture bundle'" in MAIN_TS
    assert "工作台" in MAIN_TS
    assert "加载中" in MAIN_TS
    assert "本地连续性" in MAIN_TS


def test_builtin_template_labels_can_localize_without_rewriting_user_authored_names() -> None:
    assert "function getTemplateDisplayName(" in MAIN_TS
    assert "if (template.template_type !== 'builtin')" in MAIN_TS
    assert "return template.name" in MAIN_TS
    assert "function getTemplateDisplayDescription(" in MAIN_TS
    assert "renderTextBlock(value: string | null)" in MAIN_TS
    assert "escapeHtml(value || '—')" in MAIN_TS
