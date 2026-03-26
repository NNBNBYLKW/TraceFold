from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")


def test_expense_list_uses_formal_record_consumption_language() -> None:
    assert "Expense is a formal record consumption surface. Use it to scan recorded facts first, then open a record for contextual support." in MAIN_TS
    assert "Record Scope" in MAIN_TS
    assert "Expense list stays formal-record-first. It is for reading recorded facts, not for turning the page into a chart or analytics center." in MAIN_TS
    assert "Formal Records" in MAIN_TS
    assert "Use the list to scan amount, category, recorded time, and source path before opening expense detail." in MAIN_TS


def test_expense_detail_keeps_formal_record_before_context() -> None:
    start = MAIN_TS.index("function renderExpenseDetailView(")
    end = MAIN_TS.index("function renderExpenseSourceContextSection(")
    detail_block = MAIN_TS[start:end]

    assert "title: 'Expense Record'" in detail_block
    assert "title: 'Formal Expense Record'" in detail_block
    assert "Formal expense fields remain the truth-bearing content for this page." in detail_block

    source_start = MAIN_TS.index("function renderExpenseSourceContextSection(")
    source_end = MAIN_TS.index("function renderKnowledgeDetailView(")
    source_block = MAIN_TS[source_start:source_end]

    assert "title: 'Source and Record Context'" in source_block
    assert "Source reference stays contextual support. It helps trace formal provenance without turning Expense into a workflow or analytics center." in source_block


def test_expense_source_context_links_to_upstream_capture_and_pending_when_available() -> None:
    assert 'href="/capture/${detail.source_capture_id}"' in MAIN_TS
    assert 'href="/pending/${detail.source_pending_id}"' in MAIN_TS
    assert "Capture -> Pending -> Expense" in MAIN_TS
    assert "Capture -> Expense" in MAIN_TS


def test_expense_page_copy_avoids_ai_and_analytics_expansion() -> None:
    assert "chart or analytics center" in MAIN_TS
    assert "AI finance assistant" not in MAIN_TS
    assert "Expense AI" not in MAIN_TS
