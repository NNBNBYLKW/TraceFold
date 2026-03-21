from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


DESKTOP_ENV_FILE = Path(__file__).resolve().parents[2] / ".env"


class DesktopShellSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=(str(DESKTOP_ENV_FILE), ".env"),
        env_file_encoding="utf-8",
        extra="ignore",
    )

    web_workbench_url: str = Field(..., alias="TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL")
    api_base_url: str = Field(..., alias="TRACEFOLD_DESKTOP_API_BASE_URL")
    startup_mode: str = Field("window", alias="TRACEFOLD_DESKTOP_STARTUP_MODE")
    debug: bool = Field(False, alias="TRACEFOLD_DESKTOP_DEBUG")
    log_enabled: bool = Field(True, alias="TRACEFOLD_DESKTOP_LOG_ENABLED")

    @field_validator("web_workbench_url", "api_base_url")
    @classmethod
    def validate_http_urls(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("URL must not be blank.")
        if not (normalized.startswith("http://") or normalized.startswith("https://")):
            raise ValueError("URL must start with http:// or https://.")
        return normalized.rstrip("/")

    @field_validator("startup_mode")
    @classmethod
    def validate_startup_mode(cls, value: str) -> str:
        normalized = value.strip().lower()
        if normalized not in {"window", "tray"}:
            raise ValueError("startup_mode must be either 'window' or 'tray'.")
        return normalized


@lru_cache(maxsize=1)
def get_settings() -> DesktopShellSettings:
    return DesktopShellSettings()
