from __future__ import annotations

from pathlib import Path


def test_health_detail_ai_summary_section_stays_below_fact_rule_and_source_sections() -> None:
    source = Path("apps/web/src/main.ts").read_text(encoding="utf-8")

    start = source.index("function renderHealthDetailView(")
    end = source.index("function renderDetailErrorView(")
    block = source[start:end]

    detail_position = block.index("<h2>Formal Record</h2>")
    alerts_position = block.index("renderHealthAlertSection(")
    source_position = block.index("renderSourceSection(")
    ai_position = block.index("renderHealthAiSummarySection(")

    assert detail_position < alerts_position < source_position < ai_position
    assert "AI Derivation" in source
