from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class DesktopShellSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    web_workbench_url: str = Field(..., alias="TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL")
    api_base_url: str = Field(..., alias="TRACEFOLD_DESKTOP_API_BASE_URL")
    startup_mode: str = Field("window", alias="TRACEFOLD_DESKTOP_STARTUP_MODE")
    debug: bool = Field(False, alias="TRACEFOLD_DESKTOP_DEBUG")
    log_enabled: bool = Field(True, alias="TRACEFOLD_DESKTOP_LOG_ENABLED")


@lru_cache(maxsize=1)
def get_settings() -> DesktopShellSettings:
    return DesktopShellSettings()
