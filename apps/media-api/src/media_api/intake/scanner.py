from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Literal

from sqlalchemy.orm import Session

from media_api.infrastructure.db import utc_now
from media_api.intake.repository import ScanCandidateRepository
from media_api.storage.contracts import FileSnapshot
from media_api.storage.local import LocalStorage


IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".gif"}
VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".webm"}
SUPPORTED_DIRECT_EXTENSIONS = IMAGE_EXTENSIONS | VIDEO_EXTENSIONS
IGNORED_SUFFIXES = {".part", ".crdownload", ".tmp"}


@dataclass(frozen=True)
class StableCandidate:
    path: Path
    media_type: Literal["image", "video"]
    snapshot: FileSnapshot


class InboxScanner:
    def __init__(self, *, session: Session, storage: LocalStorage) -> None:
        self.session = session
        self.storage = storage
        self.candidates = ScanCandidateRepository(session)
        self.last_observed_count = 0

    def observe(self, *, now: datetime | None = None) -> list[StableCandidate]:
        observed_at = now or utc_now()
        stable: list[StableCandidate] = []
        self.last_observed_count = 0

        for path in sorted(self.storage.inbox_dir.rglob("*")):
            if not path.is_file() or path.suffix.casefold() in IGNORED_SUFFIXES:
                continue

            self.last_observed_count += 1
            extension = path.suffix.casefold()
            media_type = _media_type(extension)
            snapshot = self.storage.snapshot(path)
            candidate = self.candidates.observe(
                snapshot,
                extension=extension,
                media_type=media_type,
                observed_at=observed_at,
            )
            if (
                candidate.is_stable
                and not candidate.is_claimed
                and media_type is not None
            ):
                stable.append(
                    StableCandidate(
                        path=snapshot.path,
                        media_type=media_type,
                        snapshot=snapshot,
                    )
                )

        self.session.commit()
        return stable


def _media_type(extension: str) -> Literal["image", "video"] | None:
    if extension in IMAGE_EXTENSIONS:
        return "image"
    if extension in VIDEO_EXTENSIONS:
        return "video"
    return None
