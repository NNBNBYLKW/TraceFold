from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_capture_list_uses_formal_upstream_visibility_language() -> None:
    assert "Capture is the visible upstream intake inbox. Use it to inspect what entered the system, what still needs follow-up, and where it flowed next." in MAIN_TS
    assert "Inbox and Triage Status" in MAIN_TS
    assert "Capture inbox makes upstream input records visible without turning the web app into a new intake platform." in MAIN_TS
    assert "Use the inbox to scan new intake, downstream linkage, and the next sensible destination before opening capture detail." in MAIN_TS


def test_capture_detail_keeps_formal_section_order() -> None:
    start = MAIN_TS.index("function renderCaptureDetailView(")
    end = MAIN_TS.index("function renderPendingListView(")
    block = MAIN_TS[start:end]

    positions = [
        block.index("Current Capture Item"),
        block.index("renderCaptureRawContentSection(detail)"),
        block.index("renderCaptureTriageContextSection(detail)"),
        block.index("renderCaptureParseContextSection(detail)"),
        block.index("renderCapturePendingLinkSection(detail)"),
        block.index("renderCaptureFormalResultSection(detail)"),
    ]

    assert positions == sorted(positions)
    assert "Capture detail keeps the upstream input readable first, then shows triage context and downstream linkage as support." in block
    assert "Triage Context and Next Step" in MAIN_TS
    assert "Chain Summary" in block
    assert "renderSectionActionRow(actionLinks)" in block


def test_capture_submission_entry_stays_restrained() -> None:
    assert "Minimal Capture Entry" in MAIN_TS
    assert "This restrained entry accepts plain text only and reuses the existing backend capture submission semantics." in MAIN_TS
    assert "Creating a capture record here does not introduce a new input platform." in MAIN_TS
    assert "Create Capture Record" in MAIN_TS


def test_capture_pending_and_formal_linkage_copy_is_present() -> None:
    assert "Pending Review Linkage" in MAIN_TS
    assert "Pending is the downstream review workbench for captures that need a formal decision." in MAIN_TS
    assert "Formal Result and Resolution Context" in MAIN_TS
    assert "Formal-result linkage shows the downstream fact record without turning Capture into a workflow console." in MAIN_TS
    assert "Continue in Pending" in MAIN_TS
    assert "View resulting record" in MAIN_TS


def test_capture_api_client_exposes_list_detail_and_submission() -> None:
    assert "fetchCaptureList" in API_TS
    assert "fetchCaptureDetail" in API_TS
    assert "submitCapture" in API_TS
    assert "request<CaptureListResponse>('/api/capture', params)" in API_TS
    assert "request<CaptureDetail>(`/api/capture/${id}`)" in API_TS
    assert "request<CaptureSubmitResult>('/api/capture', undefined, 'POST', payload)" in API_TS
    assert "pending_item_id: number | null" in API_TS
    assert "formal_record_id: number | null" in API_TS
