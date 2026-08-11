from __future__ import annotations

from pathlib import Path

from pydantic import BaseModel, Field

from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.service import ScanResult
from media_api.intake.state import InboxAction, InboxStatus, allowed_actions


class ScanResultRead(BaseModel):
    observed_candidates: int
    stable_candidates: int
    created_items: int

    @classmethod
    def from_result(cls, result: ScanResult) -> ScanResultRead:
        return cls(
            observed_candidates=result.observed_candidates,
            stable_candidates=result.stable_candidates,
            created_items=result.created_items,
        )


class SourceFileSummary(BaseModel):
    id: str
    filename: str
    extension: str
    size: int
    is_primary: bool

    @classmethod
    def from_model(cls, source: SourceFile) -> SourceFileSummary:
        return cls(
            id=source.id,
            filename=Path(source.canonical_path).name,
            extension=source.extension,
            size=source.size,
            is_primary=source.is_primary,
        )


class InboxErrorRead(BaseModel):
    code: str
    message: str


class InboxItemRead(BaseModel):
    id: str
    media_type: str
    status: InboxStatus
    allowed_actions: list[InboxAction]
    actual_filename: str
    display_name: str | None
    author_ids: list[str]
    series_id: str | None
    tag_ids: list[str]
    sources: list[SourceFileSummary]
    preview_url: str
    progress_percent: int | None
    error: InboxErrorRead | None

    @classmethod
    def from_model(cls, item: InboxItem) -> InboxItemRead:
        return cls(
            id=item.id,
            media_type=item.media_type,
            status=item.status,
            allowed_actions=sorted(
                allowed_actions(item.status),
                key=lambda action: action.value,
            ),
            actual_filename=item.suggested_actual_filename,
            display_name=item.display_name,
            author_ids=[author.id for author in item.authors],
            series_id=item.series_id,
            tag_ids=[tag.id for tag in item.tags],
            sources=[
                SourceFileSummary.from_model(source)
                for source in sorted(
                    item.sources,
                    key=lambda value: (not value.is_primary, value.canonical_path),
                )
            ],
            preview_url=f"/api/inbox/items/{item.id}/preview",
            progress_percent=None,
            error=(
                InboxErrorRead(
                    code=item.error_code or "INBOX_FAILED",
                    message=item.error_message or "操作未能完成。",
                )
                if item.error_code is not None or item.error_message is not None
                else None
            ),
        )


class InboxMetadataPatch(BaseModel):
    display_name: str | None
    author_ids: list[str]
    series_id: str | None
    tag_ids: list[str]


class BulkMetadataPatch(BaseModel):
    item_ids: list[str] = Field(min_length=1)
    append_author_ids: list[str] = Field(default_factory=list)
    append_tag_ids: list[str] = Field(default_factory=list)
    replace_series: bool = False
    replace_series_id: str | None = None


class BulkMetadataResult(BaseModel):
    updated_count: int
