from __future__ import annotations

import json
import logging
import logging.config
from datetime import date, datetime
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


def build_log_message(event: str, **fields: Any) -> str:
    """
    Build a compact structured log payload as a JSON string.
    """

    payload = {"event": event}
    for key, value in fields.items():
        if value is None:
            continue
        payload[key] = _normalize_log_value(value)
    return json.dumps(payload, ensure_ascii=True, sort_keys=True)


def log_event(
    logger: logging.Logger,
    *,
    level: int = logging.INFO,
    event: str,
    **fields: Any,
) -> None:
    """
    Emit a structured log event through the shared logger configuration.
    """

    logger.log(level, build_log_message(event, **fields))


def _normalize_log_value(value: Any) -> Any:
    if isinstance(value, datetime):
        return value.isoformat()
    if isinstance(value, date):
        return value.isoformat()
    if isinstance(value, Exception):
        return str(value)
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    if isinstance(value, dict):
        return {str(key): _normalize_log_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple, set)):
        return [_normalize_log_value(item) for item in value]
    return str(value)
