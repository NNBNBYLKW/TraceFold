from __future__ import annotations

from sqlalchemy import or_, select
from sqlalchemy.orm import Session, selectinload

from media_api.catalog.models import IngestOperation, MediaAsset
from media_api.taxonomy.models import Author, Tag


class IngestOperationRepository:
    def __init__(self, session: Session) -> None:
        self.session = session

    def by_idempotency_key(self, idempotency_key: str) -> IngestOperation | None:
        return self.session.scalar(
            select(IngestOperation).where(
                IngestOperation.idempotency_key == idempotency_key
            )
        )

    def by_resolution_idempotency_key(
        self,
        idempotency_key: str,
    ) -> IngestOperation | None:
        return self.session.scalar(
            select(IngestOperation).where(
                IngestOperation.resolution_idempotency_key == idempotency_key
            )
        )

    def get(self, operation_id: str) -> IngestOperation | None:
        return self.session.get(IngestOperation, operation_id)

    def require(self, operation_id: str) -> IngestOperation:
        operation = self.get(operation_id)
        if operation is None:
            raise LookupError(f"ingest operation not found: {operation_id}")
        return operation

    def recoverable(self) -> list[IngestOperation]:
        return list(
            self.session.scalars(
                select(IngestOperation)
                .where(
                    IngestOperation.state.in_(
                        (
                            "prepared",
                            "file_committed",
                            "db_committed",
                            "cleanup_pending",
                        )
                    ),
                    IngestOperation.conflict_kinds_json.is_(None),
                )
                .order_by(IngestOperation.created_at, IngestOperation.id)
            )
        )


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

    def list(
        self,
        *,
        search: str | None = None,
        author_id: str | None = None,
        series_id: str | None = None,
        tag_id: str | None = None,
        media_type: str | None = None,
    ) -> list[MediaAsset]:
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
        if author_id is not None:
            statement = statement.where(
                MediaAsset.authors.any(Author.id == author_id)
            )
        if series_id is not None:
            statement = statement.where(MediaAsset.series_id == series_id)
        if tag_id is not None:
            statement = statement.where(MediaAsset.tags.any(Tag.id == tag_id))
        if media_type is not None:
            statement = statement.where(MediaAsset.media_type == media_type)
        return list(self.session.scalars(statement).unique())
