from __future__ import annotations

from pathlib import Path


def test_health_detail_keeps_formal_source_and_alerts_without_ai_section() -> None:
    source = Path("apps/web/src/main.ts").read_text(encoding="utf-8")

    start = source.index("function renderHealthDetailView(")
    end = source.index("function renderDetailErrorView(")
    block = source[start:end]

    detail_position = block.index("title: 'Formal Record'")
    source_position = block.index("renderSourceSection(")
    alerts_position = block.index("renderHealthAlertSection(")

    assert detail_position < source_position < alerts_position
    assert "renderHealthAiSummarySection(" not in block
    assert "AI Derivation" not in block


def test_health_pages_do_not_include_health_ai_actions_or_copy() -> None:
    source = Path("apps/web/src/main.ts").read_text(encoding="utf-8")

    assert "rerun-health-summary" not in source
    assert "rerunHealthAiSummary" not in source
    assert "It is only available for subjective health records." not in source
