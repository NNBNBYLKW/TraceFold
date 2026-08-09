from __future__ import annotations

from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest
from sqlalchemy.orm import Session

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import ScanCandidate
from media_api.intake.scanner import InboxScanner
from media_api.storage.local import LocalStorage


class ManualClock:
    def __init__(self) -> None:
        self._now = datetime(2026, 8, 10, 9, 0, tzinfo=UTC)

    def now(self) -> datetime:
        return self._now

    def advance(self, *, seconds: int) -> datetime:
        self._now += timedelta(seconds=seconds)
        return self._now


@pytest.fixture
def session(settings: BootstrapSettings) -> Session:
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    db = session_factory()
    try:
        yield db
    finally:
        db.close()
        Base.metadata.drop_all(engine)
        engine.dispose()


@pytest.fixture
def scanner(session: Session, storage: LocalStorage) -> InboxScanner:
    return InboxScanner(session=session, storage=storage)


@pytest.fixture
def clock() -> ManualClock:
    return ManualClock()


def test_ignores_download_temporary_files(
    scanner: InboxScanner,
    storage: LocalStorage,
) -> None:
    (storage.inbox_dir / "clip.mp4.part").write_bytes(b"x")
    (storage.inbox_dir / "image.jpg.crdownload").write_bytes(b"x")

    assert scanner.observe() == []
    assert scanner.session.query(ScanCandidate).count() == 0


def test_requires_two_unchanged_observations(
    scanner: InboxScanner,
    storage: LocalStorage,
    clock: ManualClock,
) -> None:
    path = storage.inbox_dir / "photo.jpg"
    path.write_bytes(b"image")

    assert scanner.observe(now=clock.now()) == []
    stable = scanner.observe(now=clock.advance(seconds=2))

    assert [item.path.name for item in stable] == ["photo.jpg"]


def test_changed_size_resets_stability(
    scanner: InboxScanner,
    storage: LocalStorage,
    clock: ManualClock,
) -> None:
    path = storage.inbox_dir / "clip.mp4"
    path.write_bytes(b"a")
    scanner.observe(now=clock.now())
    path.write_bytes(b"ab")

    assert scanner.observe(now=clock.advance(seconds=2)) == []


def test_standalone_m4a_is_stable_but_not_a_direct_candidate(
    scanner: InboxScanner,
    storage: LocalStorage,
    clock: ManualClock,
) -> None:
    path = storage.inbox_dir / "clip.m4a"
    path.write_bytes(b"audio")

    assert scanner.observe(now=clock.now()) == []
    assert scanner.observe(now=clock.advance(seconds=2)) == []

    candidate = scanner.session.query(ScanCandidate).one()
    assert candidate.canonical_path == str(path.resolve())
    assert candidate.is_stable is True
    assert candidate.is_claimed is False
