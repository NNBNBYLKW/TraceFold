from __future__ import annotations

from dataclasses import dataclass


@dataclass(slots=True)
class IncomingMessage:
    chat_id: int
    user_id: int | None
    chat_type: str | None
    message_id: int | None
    text: str | None


@dataclass(slots=True)
class OutgoingMessage:
    chat_id: int
    text: str


@dataclass(slots=True)
class CommandContext:
    message: IncomingMessage
    command: str
    arguments: str
