from __future__ import annotations

import logging

logger = logging.getLogger("tracefold.telegram.adapter")


def log_adapter_event(
    *,
    command: str | None,
    message_type: str,
    chat_id: int,
    message_id: int | None,
    endpoint: str,
    outcome: str,
    error_code: str | None = None,
) -> None:
    logger.info(
        "telegram_adapter_event command=%s message_type=%s chat_id=%s message_id=%s endpoint=%s outcome=%s error_code=%s",
        command or "-",
        message_type,
        chat_id,
        message_id if message_id is not None else "-",
        endpoint,
        outcome,
        error_code or "-",
    )
