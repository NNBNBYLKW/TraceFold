from __future__ import annotations

from sqlalchemy.orm import Session

from media_api.catalog.conflicts import ConflictService
from media_api.catalog.models import MediaAsset
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.storage.local import LocalStorage


def test_detects_name_and_hash_conflicts(
    settings: BootstrapSettings,
    storage: LocalStorage,
) -> None:
    factory = create_engine_and_session(settings)
    engine = factory.kw["bind"]
    Base.metadata.create_all(engine)
    try:
        with factory() as session:
            existing = MediaAsset(
                media_type="video",
                actual_filename="same.mp4",
                final_relative_path="same.mp4",
                size=8,
                sha256="a" * 64,
            )
            session.add(existing)
            session.commit()

            result = ConflictService(session=session, storage=storage).detect(
                actual_filename=existing.actual_filename,
                sha256=existing.sha256,
            )

            assert result.kinds == frozenset({"name", "content"})
            assert result.existing_asset_id == existing.id
    finally:
        Base.metadata.drop_all(engine)
        engine.dispose()


def test_detects_unregistered_name_conflict_without_inventing_asset(
    settings: BootstrapSettings,
    storage: LocalStorage,
) -> None:
    factory = create_engine_and_session(settings)
    engine = factory.kw["bind"]
    Base.metadata.create_all(engine)
    try:
        unknown = storage.library_dir / "unknown.mp4"
        unknown.write_bytes(b"unknown")
        with factory() as session:
            result = ConflictService(session=session, storage=storage).detect(
                actual_filename=unknown.name,
                sha256="b" * 64,
            )

            assert result.kinds == frozenset({"name"})
            assert result.existing_asset_id is None
    finally:
        Base.metadata.drop_all(engine)
        engine.dispose()
