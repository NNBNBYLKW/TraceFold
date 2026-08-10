from __future__ import annotations

from itertools import combinations
from pathlib import Path
from urllib.parse import urlparse

from pydantic import Field, field_validator, model_validator
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
    web_origin: str | None = None

    @field_validator("web_origin")
    @classmethod
    def validate_web_origin(cls, value: str | None) -> str | None:
        if value is None:
            return None
        parsed = urlparse(value)
        if (
            parsed.scheme != "http"
            or parsed.hostname not in {"127.0.0.1", "localhost", "::1"}
            or parsed.path not in {"", "/"}
            or parsed.query
            or parsed.fragment
        ):
            raise ValueError("web_origin must be a local HTTP origin")
        return value.rstrip("/")

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
