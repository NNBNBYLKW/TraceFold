from __future__ import annotations

from functools import lru_cache

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class FeishuAdapterSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    app_id: str = Field(..., alias="TRACEFOLD_FEISHU_APP_ID")
    app_secret: str = Field(..., alias="TRACEFOLD_FEISHU_APP_SECRET")
    api_base_url: str = Field(..., alias="TRACEFOLD_FEISHU_API_BASE_URL")
    open_base_url: str = Field("https://open.feishu.cn/open-apis", alias="TRACEFOLD_FEISHU_OPEN_BASE_URL")
    timeout_seconds: float = Field(5.0, alias="TRACEFOLD_FEISHU_TIMEOUT_SECONDS")
    debug: bool = Field(False, alias="TRACEFOLD_FEISHU_DEBUG")
    log_enabled: bool = Field(True, alias="TRACEFOLD_FEISHU_LOG_ENABLED")

    @field_validator("app_id", "app_secret")
    @classmethod
    def validate_non_blank_secret_fields(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("value must not be blank.")
        return normalized

    @field_validator("api_base_url", "open_base_url")
    @classmethod
    def validate_http_base_urls(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("base url must not be blank.")
        if not (normalized.startswith("http://") or normalized.startswith("https://")):
            raise ValueError("base url must start with http:// or https://.")
        return normalized.rstrip("/")

    @field_validator("timeout_seconds")
    @classmethod
    def validate_timeout_seconds(cls, value: float) -> float:
        if value <= 0:
            raise ValueError("timeout_seconds must be greater than zero.")
        return value


@lru_cache(maxsize=1)
def get_settings() -> FeishuAdapterSettings:
    return FeishuAdapterSettings()
