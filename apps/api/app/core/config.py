from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from typing import Literal

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


ROOT_DIR = Path(__file__).resolve().parents[2]
ENV_FILE = ROOT_DIR / ".env"


class Settings(BaseSettings):
    """
    Centralized application settings for the API app.

    Rules:
    - Environment variables are read only here.
    - Other modules must import `settings` or call `get_settings()`.
    - Do not call os.getenv() outside this file.
    """

    api_env: Literal["development", "test", "production"] = Field(
        default="development",
        alias="API_ENV",
    )
    api_host: str = Field(
        default="127.0.0.1",
        alias="API_HOST",
    )
    api_port: int = Field(
        default=8000,
        alias="API_PORT",
    )
    api_log_level: Literal["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"] = Field(
        default="INFO",
        alias="API_LOG_LEVEL",
    )
    api_database_url: str = Field(
        default="sqlite:///../../data/app.db",
        alias="API_DATABASE_URL",
    )

    model_config = SettingsConfigDict(
        env_file=ENV_FILE,
        env_file_encoding="utf-8",
        case_sensitive=True,
        extra="ignore",
        populate_by_name=True,
    )

    @property
    def is_development(self) -> bool:
        return self.api_env == "development"

    @property
    def is_test(self) -> bool:
        return self.api_env == "test"

    @property
    def is_production(self) -> bool:
        return self.api_env == "production"


@lru_cache
def get_settings() -> Settings:
    return Settings()


settings = get_settings()