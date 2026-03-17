from .clients.status_client import TraceFoldStatusClient
from .core.config import get_settings
from .shell.app import DesktopShellApp


def create_app() -> DesktopShellApp:
    settings = get_settings()
    return DesktopShellApp(
        settings=settings,
        status_client=TraceFoldStatusClient(
            base_url=settings.api_base_url,
            timeout_seconds=5.0,
        ),
    )


def main() -> None:
    app = create_app()
    try:
        app.bootstrap()
    finally:
        app.close()


if __name__ == "__main__":
    main()
