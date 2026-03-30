from __future__ import annotations

from .handlers import TelegramMessageHandler
from ..clients.telegram_api import TelegramApiClient
from ..clients.tracefold_api import TraceFoldApiClient
from ..core.config import TelegramAdapterSettings


class TelegramAdapterApp:
    def __init__(
        self,
        *,
        settings: TelegramAdapterSettings,
        tracefold_api: TraceFoldApiClient,
        telegram_api: TelegramApiClient,
        handler: TelegramMessageHandler | None = None,
    ) -> None:
        self.settings = settings
        self.tracefold_api = tracefold_api
        self.telegram_api = telegram_api
        self.handler = handler or TelegramMessageHandler(tracefold_api=tracefold_api)

    def startup_summary(self) -> dict[str, object]:
        return {
            "api_base_url": self.settings.api_base_url,
            "timeout_seconds": self.settings.timeout_seconds,
            "debug": self.settings.debug,
            "log_enabled": self.settings.log_enabled,
        }

    def probe_dependencies(self) -> dict[str, object]:
        return {
            "tracefold_api": self.tracefold_api.get_health_status(),
            "telegram_identity": self.telegram_api.get_me(),
        }

    def process_updates_once(self, *, offset: int | None = None) -> int | None:
        return self.process_updates_once_with_timeout(offset=offset, timeout_seconds=int(self.settings.timeout_seconds))

    def process_updates_once_with_timeout(
        self,
        *,
        offset: int | None = None,
        timeout_seconds: int = 0,
    ) -> int | None:
        updates = self.telegram_api.get_updates(offset=offset, timeout_seconds=timeout_seconds)
        next_offset = offset

        for update in updates:
            update_id = update.get("update_id")
            if isinstance(update_id, int):
                next_offset = update_id + 1
            self.handle_update(update)

        return next_offset

    def run_polling(self, *, offset: int | None = None, max_iterations: int | None = None) -> int | None:
        next_offset = offset
        iterations = 0
        timeout_seconds = max(1, int(self.settings.timeout_seconds))

        while max_iterations is None or iterations < max_iterations:
            next_offset = self.process_updates_once_with_timeout(
                offset=next_offset,
                timeout_seconds=timeout_seconds,
            )
            iterations += 1

        return next_offset

    def handle_update(self, update: dict) -> None:
        outgoing = self.handler.handle_update(update)
        if outgoing is None:
            return

        self.telegram_api.send_text_message(outgoing.chat_id, outgoing.text)

    def close(self) -> None:
        self.tracefold_api.close()
        self.telegram_api.close()
