from __future__ import annotations

from .handlers import FeishuMessageHandler
from ..clients.feishu_api import FeishuApiClient
from ..clients.tracefold_api import TraceFoldApiClient
from ..core.config import FeishuAdapterSettings


class FeishuAdapterApp:
    def __init__(
        self,
        *,
        settings: FeishuAdapterSettings,
        tracefold_api: TraceFoldApiClient,
        feishu_api: FeishuApiClient,
        handler: FeishuMessageHandler | None = None,
    ) -> None:
        self.settings = settings
        self.tracefold_api = tracefold_api
        self.feishu_api = feishu_api
        self.handler = handler or FeishuMessageHandler(tracefold_api=tracefold_api)

    def handle_callback(self, payload: dict) -> dict[str, object]:
        if payload.get("type") == "url_verification":
            return {"challenge": payload.get("challenge", "")}

        outgoing = self.handler.handle_event(payload)
        if outgoing is not None:
            self.feishu_api.reply_text_message(message_id=outgoing.message_id, text=outgoing.text)

        return {"code": 0}

    def close(self) -> None:
        self.tracefold_api.close()
        self.feishu_api.close()
