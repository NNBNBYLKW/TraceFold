from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_capture_list_uses_formal_upstream_visibility_language() -> None:
    assert "Capture is the visible upstream input record layer. Use it to inspect what entered the system and where it flowed next." in MAIN_TS
    assert "Capture Status" in MAIN_TS
    assert "Capture list makes upstream input records visible without turning the web app into a new intake platform." in MAIN_TS
    assert "Use the list to scan source, type, current stage, and timestamps before opening capture detail." in MAIN_TS


def test_capture_detail_keeps_formal_section_order() -> None:
    start = MAIN_TS.index("function renderCaptureDetailView(")
    end = MAIN_TS.index("function renderPendingListView(")
    block = MAIN_TS[start:end]

    positions = [
        block.index("title: 'Current Capture Item'"),
        block.index("renderCaptureRawContentSection(detail)"),
        block.index("renderCaptureParseContextSection(detail)"),
        block.index("renderCapturePendingLinkSection(detail)"),
        block.index("renderCaptureFormalResultSection(detail)"),
    ]

    assert positions == sorted(positions)
    assert "Capture detail keeps the upstream input visible, then shows how that input moved toward Pending review or a formal result." in block


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


def test_capture_api_client_exposes_list_detail_and_submission() -> None:
    assert "fetchCaptureList" in API_TS
    assert "fetchCaptureDetail" in API_TS
    assert "submitCapture" in API_TS
    assert "request<CaptureListResponse>('/api/capture', params)" in API_TS
    assert "request<CaptureDetail>(`/api/capture/${id}`)" in API_TS
    assert "request<CaptureSubmitResult>('/api/capture', undefined, 'POST', payload)" in API_TS
