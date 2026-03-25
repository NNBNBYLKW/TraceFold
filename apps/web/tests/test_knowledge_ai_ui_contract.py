from __future__ import annotations

from pathlib import Path


def test_knowledge_detail_ai_summary_section_stays_below_formal_content_and_source() -> None:
    source = Path("apps/web/src/main.ts").read_text(encoding="utf-8")

    start = source.index("function renderKnowledgeDetailView(")
    end = source.index("function renderHealthDetailView(")
    block = source[start:end]

    content_position = block.index("title: 'Formal Content'")
    source_position = block.index("title: 'Source Reference'")
    ai_position = block.index("renderKnowledgeAiSummarySection(")

    assert content_position < source_position < ai_position
    assert "function renderKnowledgeAiSummarySection(" in source
    assert "AI-derived Summary" in source
    assert "Read the formal record first, then use source context and AI-derived summary as support." in source
