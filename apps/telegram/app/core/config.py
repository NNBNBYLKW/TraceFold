from __future__ import annotations

from functools import lru_cache

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class TelegramAdapterSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    bot_token: str = Field(..., alias="TRACEFOLD_TELEGRAM_BOT_TOKEN")
    api_base_url: str = Field(..., alias="TRACEFOLD_TELEGRAM_API_BASE_URL")
    timeout_seconds: float = Field(5.0, alias="TRACEFOLD_TELEGRAM_TIMEOUT_SECONDS")
    debug: bool = Field(False, alias="TRACEFOLD_TELEGRAM_DEBUG")
    log_enabled: bool = Field(True, alias="TRACEFOLD_TELEGRAM_LOG_ENABLED")

    @field_validator("bot_token")
    @classmethod
    def validate_bot_token(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("bot_token must not be blank.")
        return normalized

    @field_validator("api_base_url")
    @classmethod
    def validate_api_base_url(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("api_base_url must not be blank.")
        if not (normalized.startswith("http://") or normalized.startswith("https://")):
            raise ValueError("api_base_url must start with http:// or https://.")
        return normalized.rstrip("/")

    @field_validator("timeout_seconds")
    @classmethod
    def validate_timeout_seconds(cls, value: float) -> float:
        if value <= 0:
            raise ValueError("timeout_seconds must be greater than zero.")
        return value


@lru_cache(maxsize=1)
def get_settings() -> TelegramAdapterSettings:
    return TelegramAdapterSettings()
