from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")


def test_main_surface_page_copy_includes_zh_en_coverage_for_workbench_pending_and_capture() -> None:
    assert "3. Common Entry Paths" in MAIN_TS
    assert "常用入口路径" in MAIN_TS
    assert "5. Recent Context" in MAIN_TS
    assert "最近上下文" in MAIN_TS
    assert "Current Pending Item" in MAIN_TS
    assert "当前待审项" in MAIN_TS
    assert "Current Review Payload" in MAIN_TS
    assert "当前复核载荷" in MAIN_TS
    assert "Current Capture Item" in MAIN_TS
    assert "当前采集项" in MAIN_TS
    assert "Capture Inbox" in MAIN_TS
    assert "采集收件箱" in MAIN_TS
    assert "Triage Context and Next Step" in MAIN_TS
    assert "分流上下文与下一步" in MAIN_TS
    assert "Raw Input and Captured Content" in MAIN_TS
    assert "原始输入与采集内容" in MAIN_TS


def test_main_surface_page_copy_includes_zh_en_coverage_for_expense_knowledge_and_health() -> None:
    assert "Formal Expense Record" in MAIN_TS
    assert "正式支出记录" in MAIN_TS
    assert "Source and Record Context" in MAIN_TS
    assert "来源与记录上下文" in MAIN_TS
    assert "Formal Content" in MAIN_TS
    assert "正式内容" in MAIN_TS
    assert "Formal Body" in MAIN_TS
    assert "正式正文" in MAIN_TS
    assert "AI-derived Summary" in MAIN_TS
    assert "AI 派生摘要" in MAIN_TS
    assert "Formal Record" in MAIN_TS
    assert "正式记录" in MAIN_TS
    assert "Recorded Value" in MAIN_TS
    assert "记录值" in MAIN_TS
    assert "Rule Alerts" in MAIN_TS
    assert "规则提醒" in MAIN_TS


def test_quick_capture_page_copy_includes_zh_en_coverage() -> None:
    assert "Quick Capture" in MAIN_TS
    assert "快速采集" in MAIN_TS
    assert "Quick Text Entry" in MAIN_TS
    assert "快速文本录入" in MAIN_TS
    assert "Chain Note" in MAIN_TS
    assert "链路说明" in MAIN_TS


def test_bulk_intake_page_copy_includes_zh_en_coverage() -> None:
    assert "Bulk Intake" in MAIN_TS
    assert "批量导入" in MAIN_TS
    assert "Text File Intake" in MAIN_TS
    assert "文本文件导入" in MAIN_TS
    assert "Preview Before Import" in MAIN_TS
    assert "导入前预览" in MAIN_TS


def test_locale_page_coverage_keeps_user_and_ai_content_rendered_from_raw_values() -> None:
    assert "return template.name" in MAIN_TS
    assert "renderTextBlock(detail.raw_text)" in MAIN_TS
    assert "renderTextBlock(detail.content)" in MAIN_TS
    assert "renderTextBlock(summaryText)" in MAIN_TS
