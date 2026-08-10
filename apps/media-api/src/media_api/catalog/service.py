from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Protocol

from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.catalog.models import MediaAsset
from media_api.catalog.repository import MediaAssetRepository
from media_api.storage.contracts import StoragePort, UnsafePathError
from media_api.storage.local import ensure_within
from media_api.taxonomy.models import Author, Series, Tag


class CatalogError(ValueError):
    pass


class MediaAssetNotFound(CatalogError):
    pass


class CatalogTaxonomyNotFound(CatalogError):
    pass


class UnsafeRegisteredAssetPath(CatalogError):
    pass


class RegisteredAssetFileMissing(CatalogError):
    pass


class RevealFailed(CatalogError):
    pass


class RevealPort(Protocol):
    def reveal(self, path: Path) -> None: ...


class PlatformRevealPort:
    def reveal(self, path: Path) -> None:
        if sys.platform == "win32":
            command = ["explorer.exe", f"/select,{path}"]
        elif sys.platform == "darwin":
            command = ["open", "-R", str(path)]
        else:
            command = ["xdg-open", str(path.parent)]
        try:
            subprocess.Popen(command)
        except OSError as exc:
            raise RevealFailed("could not open the system file manager") from exc


class CatalogService:
    def __init__(
        self,
        session: Session,
        *,
        storage: StoragePort | None = None,
        reveal_port: RevealPort | None = None,
    ) -> None:
        self.session = session
        self.assets = MediaAssetRepository(session)
        self.storage = storage
        self.reveal_port = reveal_port

    def list_assets(
        self,
        *,
        search: str | None = None,
        author_id: str | None = None,
        series_id: str | None = None,
        tag_id: str | None = None,
        media_type: str | None = None,
    ) -> list[MediaAsset]:
        return self.assets.list(
            search=search,
            author_id=author_id,
            series_id=series_id,
            tag_id=tag_id,
            media_type=media_type,
        )

    def get_asset(self, asset_id: str) -> MediaAsset:
        asset = self.assets.get(asset_id)
        if asset is None:
            raise MediaAssetNotFound(asset_id)
        return asset

    def reveal_asset(self, asset_id: str) -> Path:
        asset = self.get_asset(asset_id)
        if self.storage is None or self.reveal_port is None:
            raise RuntimeError("reveal dependencies are not configured")
        try:
            registered_path = ensure_within(
                self.storage.library_dir / asset.final_relative_path,
                self.storage.library_dir,
            )
        except UnsafePathError as exc:
            raise UnsafeRegisteredAssetPath(asset.id) from exc
        if registered_path.parent != self.storage.library_dir:
            raise UnsafeRegisteredAssetPath(asset.id)
        if not registered_path.is_file():
            raise RegisteredAssetFileMissing(asset.id)
        self.reveal_port.reveal(registered_path)
        return registered_path

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
