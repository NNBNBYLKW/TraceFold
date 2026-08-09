from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.catalog.models import MediaAsset
from media_api.catalog.repository import MediaAssetRepository
from media_api.taxonomy.models import Author, Series, Tag


class CatalogError(ValueError):
    pass


class MediaAssetNotFound(CatalogError):
    pass


class CatalogTaxonomyNotFound(CatalogError):
    pass


class CatalogService:
    def __init__(self, session: Session) -> None:
        self.session = session
        self.assets = MediaAssetRepository(session)

    def list_assets(self, *, search: str | None = None) -> list[MediaAsset]:
        return self.assets.list(search=search)

    def get_asset(self, asset_id: str) -> MediaAsset:
        asset = self.assets.get(asset_id)
        if asset is None:
            raise MediaAssetNotFound(asset_id)
        return asset

    def update_metadata(
        self,
        asset_id: str,
        *,
        display_name: str | None,
        author_ids: list[str],
        series_id: str | None,
        tag_ids: list[str],
    ) -> MediaAsset:
        asset = self.get_asset(asset_id)
        authors = self._resolve_many(Author, author_ids)
        series = self._resolve_one(Series, series_id)
        tags = self._resolve_many(Tag, tag_ids)

        asset.display_name = display_name
        asset.authors = authors
        asset.series = series
        asset.tags = tags
        self.session.commit()
        return asset

    def _resolve_many(self, model: type, entry_ids: list[str]) -> list:
        unique_ids = list(dict.fromkeys(entry_ids))
        if not unique_ids:
            return []
        entries = list(
            self.session.scalars(select(model).where(model.id.in_(unique_ids)))
        )
        by_id = {entry.id: entry for entry in entries}
        missing = [entry_id for entry_id in unique_ids if entry_id not in by_id]
        if missing:
            raise CatalogTaxonomyNotFound(missing[0])
        return [by_id[entry_id] for entry_id in unique_ids]

    def _resolve_one(self, model: type, entry_id: str | None):
        if entry_id is None:
            return None
        entry = self.session.get(model, entry_id)
        if entry is None:
            raise CatalogTaxonomyNotFound(entry_id)
        return entry
