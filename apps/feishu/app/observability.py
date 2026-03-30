from __future__ import annotations

import logging

logger = logging.getLogger("tracefold.feishu.adapter")


def log_adapter_event(
    *,
    command: str | None,
    message_type: str,
    chat_id: str,
    message_id: str | None,
    endpoint: str,
    outcome: str,
    error_code: str | None = None,
) -> None:
    logger.info(
        "feishu_adapter_event command=%s message_type=%s chat_id=%s message_id=%s endpoint=%s outcome=%s error_code=%s",
        command or "-",
        message_type,
        chat_id,
        message_id or "-",
        endpoint,
        outcome,
        error_code or "-",
    )
