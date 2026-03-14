from __future__ import annotations

import logging
import logging.config
from typing import Any

from app.core.config import settings


def build_logging_config() -> dict[str, Any]:
    """
    Build the logging configuration dictionary for the API app.

    Rules:
    - Use one centralized logging setup entry.
    - Keep console logging simple for Phase 1.
    - Reuse the configured log level from settings.
    """
    log_level = settings.api_log_level.upper()

    return {
        "version": 1,
        "disable_existing_loggers": False,
        "formatters": {
            "standard": {
                "format": "%(asctime)s | %(levelname)s | %(name)s | %(message)s",
            },
        },
        "handlers": {
            "console": {
                "class": "logging.StreamHandler",
                "level": log_level,
                "formatter": "standard",
            },
        },
        "loggers": {
            "app": {
                "handlers": ["console"],
                "level": log_level,
                "propagate": False,
            },
            "uvicorn": {
                "handlers": ["console"],
                "level": log_level,
                "propagate": False,
            },
            "uvicorn.error": {
                "handlers": ["console"],
                "level": log_level,
                "propagate": False,
            },
            "uvicorn.access": {
                "handlers": ["console"],
                "level": log_level,
                "propagate": False,
            },
        },
        "root": {
            "handlers": ["console"],
            "level": log_level,
        },
    }


def setup_logging() -> None:
    """
    Apply the centralized logging configuration.
    """
    logging.config.dictConfig(build_logging_config())


def get_logger(name: str) -> logging.Logger:
    """
    Return a logger instance for the given module or component name.
    """
    return logging.getLogger(name)