from .bot.app import TelegramAdapterApp
from .clients.telegram_api import TelegramApiClient
from .clients.tracefold_api import TraceFoldApiClient
from .core.config import get_settings


def create_app() -> TelegramAdapterApp:
    settings = get_settings()
    return TelegramAdapterApp(
        settings=settings,
        tracefold_api=TraceFoldApiClient(
            base_url=settings.api_base_url,
            timeout_seconds=settings.timeout_seconds,
        ),
        telegram_api=TelegramApiClient(
            bot_token=settings.bot_token,
            timeout_seconds=settings.timeout_seconds,
        ),
    )


def main() -> None:
    app = create_app()
    try:
        app.probe_dependencies()
    finally:
        app.close()


if __name__ == "__main__":
    main()
