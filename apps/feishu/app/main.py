from __future__ import annotations

from contextlib import asynccontextmanager
from hmac import compare_digest

from fastapi import FastAPI, HTTPException, Request

from .bot.app import FeishuAdapterApp
from .clients.feishu_api import FeishuApiClient
from .clients.tracefold_api import TraceFoldApiClient
from .core.config import get_settings


def create_runtime() -> FeishuAdapterApp:
    settings = get_settings()
    return FeishuAdapterApp(
        settings=settings,
        tracefold_api=TraceFoldApiClient(
            base_url=settings.api_base_url,
            timeout_seconds=settings.timeout_seconds,
        ),
        feishu_api=FeishuApiClient(
            app_id=settings.app_id,
            app_secret=settings.app_secret,
            open_base_url=settings.open_base_url,
            timeout_seconds=settings.timeout_seconds,
        ),
    )


def create_app(runtime: FeishuAdapterApp | None = None) -> FastAPI:
    @asynccontextmanager
    async def lifespan(app_instance: FastAPI):
        app_instance.state.runtime = runtime or create_runtime()
        try:
            yield
        finally:
            app_instance.state.runtime.close()

    app_instance = FastAPI(title="TraceFold Feishu Adapter", lifespan=lifespan)

    @app_instance.post("/feishu/events")
    async def handle_feishu_events(request: Request) -> dict[str, object]:
        payload = await request.json()
        if not isinstance(payload, dict):
            raise HTTPException(status_code=400, detail="Invalid Feishu event payload.")

        # Authenticate the callback before it can create a capture or send a reply.
        if payload.get("type") == "url_verification":
            token = payload.get("token")
        else:
            header = payload.get("header")
            token = header.get("token") if isinstance(header, dict) else None
        expected = request.app.state.runtime.settings.verification_token
        try:
            authenticated = isinstance(token, str) and bool(token) and compare_digest(
                token.encode("utf-8"), expected.encode("utf-8")
            )
        except UnicodeEncodeError:
            authenticated = False
        if not authenticated:
            raise HTTPException(status_code=403, detail="Invalid Feishu verification token.")
        return request.app.state.runtime.handle_callback(payload)

    return app_instance


app = create_app()
