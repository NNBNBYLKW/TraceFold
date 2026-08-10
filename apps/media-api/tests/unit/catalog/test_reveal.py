from __future__ import annotations

from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.catalog.models import MediaAsset
from media_api.catalog.service import CatalogService, UnsafeRegisteredAssetPath
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.storage.local import LocalStorage


class FakeRevealPort:
    def __init__(self) -> None:
        self.paths: list[Path] = []

    def reveal(self, path: Path) -> None:
        self.paths.append(path)


@pytest.fixture
def session(settings) -> Session:
    factory = create_engine_and_session(settings)
    engine = factory.kw["bind"]
    Base.metadata.create_all(engine)
    db = factory()
    try:
        yield db
    finally:
        db.close()
        Base.metadata.drop_all(engine)
        engine.dispose()


def test_reveal_passes_registered_formal_path_to_port(
    session: Session,
    storage: LocalStorage,
) -> None:
    formal = storage.library_dir / "registered.mp4"
    formal.write_bytes(b"video")
    asset = MediaAsset(
        media_type="video",
        actual_filename=formal.name,
        final_relative_path=formal.name,
        size=formal.stat().st_size,
        sha256=storage.sha256(formal),
    )
    session.add(asset)
    session.commit()
    port = FakeRevealPort()

    CatalogService(session, storage=storage, reveal_port=port).reveal_asset(asset.id)

    assert port.paths == [formal.resolve()]


def test_reveal_rejects_registered_path_that_escapes_library(
    session: Session,
    storage: LocalStorage,
) -> None:
    outside = storage.work_dir / "outside.mp4"
    outside.write_bytes(b"video")
    asset = MediaAsset(
        media_type="video",
        actual_filename=outside.name,
        final_relative_path="../work/outside.mp4",
        size=outside.stat().st_size,
        sha256=storage.sha256(outside),
    )
    session.add(asset)
    session.commit()
    port = FakeRevealPort()

    with pytest.raises(UnsafeRegisteredAssetPath):
        CatalogService(session, storage=storage, reveal_port=port).reveal_asset(asset.id)

    assert port.paths == []
