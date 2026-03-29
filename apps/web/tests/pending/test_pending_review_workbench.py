from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_pending_list_uses_formal_review_queue_language() -> None:
    assert "Pending is the review queue for scanning, prioritizing, and entering the single-item review workbench." in MAIN_TS
    assert "Review Queue" in MAIN_TS
    assert "Open items remain actionable. Confirmed, discarded, and forced items stay readable as resolution context only." in MAIN_TS
    assert "Use the list to scan status, domain, current summary, and timestamps before opening the detail workbench." in MAIN_TS


def test_pending_detail_keeps_workbench_sections_in_frozen_order() -> None:
    start = MAIN_TS.index("function renderPendingDetailView(")
    end = MAIN_TS.index("function renderExpenseListView(")
    block = MAIN_TS[start:end]

    positions = [
        block.index("Current Pending Item"),
        block.index("renderPendingCurrentPayloadSection(detail)"),
        block.index("renderPendingSourceContextSection(detail)"),
        block.index("renderPendingActionSection(detail)"),
        block.index("renderPendingHistorySection(detail)"),
    ]

    assert positions == sorted(positions)
    assert "Pending detail is a single-item review workbench. Keep the current payload primary, then use source context, actions, and history as support." in block
    assert "Review Reason" in block
    assert "Linked Formal Result" in block
    assert "Recorded Review Actions" in block


def test_pending_action_zone_copy_preserves_backend_review_semantics() -> None:
    assert "Fix updates corrected payload, does not directly write a formal record, and keeps the item reviewable afterward." in MAIN_TS
    assert "Confirm writes the current effective payload to the formal record and resolves the pending item." in MAIN_TS
    assert "Discard resolves the pending item without writing a formal record." in MAIN_TS
    assert "Force Insert writes the current effective payload through the backend force-insert path and resolves the pending item." in MAIN_TS


def test_pending_resolved_state_and_feedback_stay_inside_detail_context() -> None:
    assert "Pending item is no longer actionable." in MAIN_TS
    assert "This pending item is resolved as" in MAIN_TS
    assert "Action Complete" in MAIN_TS
    assert "Action Failed" in MAIN_TS
    assert "function renderPendingReviewFeedback(" in MAIN_TS


def test_pending_api_client_exposes_detail_and_all_review_actions() -> None:
    assert "fetchPendingList" in API_TS
    assert "fetchPendingDetail" in API_TS
    assert "confirmPending" in API_TS
    assert "discardPending" in API_TS
    assert "fixPending" in API_TS
    assert "forceInsertPending" in API_TS
    assert "/api/pending/${id}/force_insert" in API_TS
