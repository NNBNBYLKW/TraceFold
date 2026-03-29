from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_knowledge_detail_uses_formal_detail_and_formal_derivation_detail_api() -> None:
    assert "fetchKnowledgeDetail(id)" in MAIN_TS
    assert "fetchAiDerivationDetail('knowledge', id)" in MAIN_TS
    assert "requestAiDerivationRecompute('knowledge', recordId)" in MAIN_TS
    assert "export async function fetchAiDerivationDetail(" in API_TS
    assert "export async function requestAiDerivationRecompute(" in API_TS


def test_knowledge_detail_distinguishes_ready_failed_invalidated_and_not_generated_states() -> None:
    assert "AI-derived summary is not available yet." in MAIN_TS
    assert "The formal content remains available." in MAIN_TS
    assert "AI-derived summary generation failed. The formal content remains available." in MAIN_TS
    assert "AI-derived summary is invalidated and should be recomputed before relying on it." in MAIN_TS
    assert "AI-derived summary recompute has been requested. Refresh the page if the status remains pending." in MAIN_TS
    assert "AI-derived summary is unavailable right now." in MAIN_TS


def test_knowledge_detail_keeps_formal_content_as_primary_section() -> None:
    start = MAIN_TS.index("function renderKnowledgeDetailView(")
    end = MAIN_TS.index("function renderHealthDetailView(")
    block = MAIN_TS[start:end]

    formal_position = block.index("Formal Content")
    source_position = block.index("Source Reference")
    ai_position = block.index("renderKnowledgeAiSummarySection(")

    assert formal_position < source_position < ai_position
    assert "Formal content remains the record of truth for this knowledge entry." in block
    assert "Formal Body" in block
    assert "Source Text Snapshot" in block
    assert "Generated summary" in MAIN_TS
    assert "Derivation Context" in MAIN_TS
    assert "Summary" in MAIN_TS
    assert "Recompute AI-derived Summary" in MAIN_TS
