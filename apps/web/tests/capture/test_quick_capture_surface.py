from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")


def test_quick_capture_route_and_bilingual_page_copy_are_present() -> None:
    assert "case 'quick-capture':" in MAIN_TS
    assert "kind: 'quick-capture'" in MAIN_TS
    assert "Quick Capture keeps text-first intake light." in MAIN_TS
    assert "快速采集让以文本为先的录入保持轻量。" in MAIN_TS
    assert "Quick Text Entry" in MAIN_TS
    assert "快速文本录入" in MAIN_TS


def test_quick_capture_workbench_entry_stays_restrained() -> None:
    assert 'href="/quick-capture"' in MAIN_TS
    assert "Fastest way to send one text into Capture and stay ready for the next item." in MAIN_TS
    assert "这是把一段文本送入采集链路并保持准备录入下一条的最快方式。" in MAIN_TS
    assert "Web only" in MAIN_TS
    assert "仅限 Web" in MAIN_TS


def test_quick_capture_submission_keeps_user_on_page_and_ready_for_next_entry() -> None:
    start = MAIN_TS.index("async function handleQuickCaptureSubmit(")
    end = MAIN_TS.index("async function handleCaptureSubmit(")
    block = MAIN_TS[start:end]

    assert "quickCaptureUiState.draftText = rawText" in block
    assert "quickCaptureUiState.draftText = ''" in block
    assert "await renderApp()" in block
    assert "navigate(`/capture/${result.capture_id}`)" not in block
    assert "You can enter the next text now." in block
    assert "现在可以继续输入下一条。" in block


def test_quick_capture_failure_feedback_preserves_input_and_retry_path() -> None:
    start = MAIN_TS.index("async function handleQuickCaptureSubmit(")
    end = MAIN_TS.index("async function handleCaptureSubmit(")
    block = MAIN_TS[start:end]

    assert "Quick capture could not be submitted." in block
    assert "无法提交快速采集。" in block
    assert "Enter text first so the system can create a capture record." in block
    assert "请先输入文本，系统才能创建采集记录。" in block


def test_quick_capture_chain_note_preserves_capture_first_semantics() -> None:
    assert "This creates a capture record first. Parsing, pending review, or formal commit may happen later through the existing chain." in MAIN_TS
    assert "这里会先创建采集记录。解析、待审复核或正式提交会稍后通过现有链路发生。" in MAIN_TS
    assert "Capture record" in MAIN_TS
    assert "采集记录" in MAIN_TS
