from __future__ import annotations

from itertools import combinations
from pathlib import Path

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class BootstrapSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="TRACEFOLD_MEDIA_",
        env_file=".env",
        extra="ignore",
    )

    inbox_dir: Path = Path("data/media/inbox")
    library_dir: Path = Path("data/media/library")
    work_dir: Path = Path("data/media/work")
    database_path: Path = Path("data/media/media.sqlite3")
    scan_interval_seconds: int = Field(default=10, ge=2, le=3600)

    @model_validator(mode="after")
    def validate_paths(self) -> BootstrapSettings:
        inbox = self.inbox_dir.resolve()
        library = self.library_dir.resolve()
        work = self.work_dir.resolve()

        for left, right in combinations((inbox, library, work), 2):
            if left == right or left in right.parents or right in left.parents:
                raise ValueError("runtime directories must not overlap")

        database = self.database_path.resolve()
        if database == inbox or inbox in database.parents:
            raise ValueError("database_path must not be inside inbox_dir")

        return self
