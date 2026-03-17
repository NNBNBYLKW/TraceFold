from functools import lru_cache

from pydantic import Field
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


@lru_cache(maxsize=1)
def get_settings() -> TelegramAdapterSettings:
    return TelegramAdapterSettings()
