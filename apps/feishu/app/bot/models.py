from __future__ import annotations

from dataclasses import dataclass


@dataclass(slots=True)
class IncomingMessage:
    chat_id: str
    user_id: str | None
    chat_type: str | None
    message_id: str
    text: str | None


@dataclass(slots=True)
class OutgoingMessage:
    message_id: str
    chat_id: str
    text: str
