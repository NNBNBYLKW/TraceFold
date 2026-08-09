from __future__ import annotations

from sqlalchemy import or_, select
from sqlalchemy.orm import Session, selectinload

from media_api.catalog.models import IngestOperation, MediaAsset


class IngestOperationRepository:
    def __init__(self, session: Session) -> None:
        self.session = session

    def by_idempotency_key(self, idempotency_key: str) -> IngestOperation | None:
        return self.session.scalar(
            select(IngestOperation).where(
                IngestOperation.idempotency_key == idempotency_key
            )
        )

    def get(self, operation_id: str) -> IngestOperation | None:
        return self.session.get(IngestOperation, operation_id)


class MediaAssetRepository:
    def __init__(self, session: Session) -> None:
        self.session = session

    def get(self, asset_id: str) -> MediaAsset | None:
        return self.session.scalar(
            select(MediaAsset)
            .where(MediaAsset.id == asset_id)
            .options(
                selectinload(MediaAsset.authors),
                selectinload(MediaAsset.tags),
            )
        )

    def by_actual_filename(self, actual_filename: str) -> MediaAsset | None:
        return self.session.scalar(
            select(MediaAsset).where(
                MediaAsset.actual_filename == actual_filename
            )
        )

    def by_sha256(self, digest: str) -> MediaAsset | None:
        return self.session.scalar(
            select(MediaAsset).where(MediaAsset.sha256 == digest)
        )

    def list(self, *, search: str | None = None) -> list[MediaAsset]:
        statement = (
            select(MediaAsset)
            .options(
                selectinload(MediaAsset.authors),
                selectinload(MediaAsset.tags),
            )
            .order_by(
                MediaAsset.ingested_at.desc(),
                MediaAsset.id,
            )
        )
        if search is not None and search.strip():
            pattern = f"%{search.strip()}%"
            statement = statement.where(
                or_(
                    MediaAsset.display_name.ilike(pattern),
                    MediaAsset.actual_filename.ilike(pattern),
                )
            )
        return list(
            self.session.scalars(statement).unique()
        )
