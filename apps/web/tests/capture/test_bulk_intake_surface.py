from __future__ import annotations

from pathlib import Path


MAIN_TS = Path("apps/web/src/main.ts").read_text(encoding="utf-8")
API_TS = Path("apps/web/src/api.ts").read_text(encoding="utf-8")


def test_bulk_intake_route_and_workbench_entry_are_present() -> None:
    assert "case 'bulk-intake':" in MAIN_TS
    assert "kind: 'bulk-intake'" in MAIN_TS
    assert 'href="/bulk-intake"' in MAIN_TS
    assert "Import a text file, preview the candidate entries, then send them into Capture without bypassing later review." in MAIN_TS


def test_bulk_intake_page_uses_preview_before_import_copy() -> None:
    assert "Bulk Intake imports text files into Capture first." in MAIN_TS
    assert "Preview Before Import" in MAIN_TS
    assert "Review the candidate count and short previews here." in MAIN_TS
    assert "Generate preview" in MAIN_TS
    assert "Import valid entries" in MAIN_TS


def test_bulk_intake_keeps_capture_first_and_text_file_only_semantics() -> None:
    assert "Capture layer only" in MAIN_TS
    assert "Blank-line blocks" in MAIN_TS
    assert "This first version accepts .txt and .md files only." in MAIN_TS
    assert "Import creates capture records first. Parsing and review happen later through the existing chain." in MAIN_TS


def test_bulk_intake_submit_handlers_use_preview_then_import_api_contract() -> None:
    assert "previewBulkCapture" in API_TS
    assert "importBulkCapture" in API_TS
    assert "request<BulkCapturePreviewResult>('/api/capture/bulk-intake/preview'" in API_TS
    assert "request<BulkCaptureImportResult>('/api/capture/bulk-intake/import'" in API_TS
    assert "async function handleBulkIntakePreviewSubmit(" in MAIN_TS
    assert "async function handleBulkIntakeImportSubmit(" in MAIN_TS


def test_bulk_intake_import_feedback_stays_result_oriented() -> None:
    assert "Bulk intake completed." in MAIN_TS
    assert "Imported into Pending" in MAIN_TS
    assert "Committed directly under existing backend rules" in MAIN_TS
    assert "Skipped before or during import" in MAIN_TS
