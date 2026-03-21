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


def _emit_startup_feedback(snapshot: dict[str, object]) -> None:
    print("TraceFold Desktop shell is running.")
    print(f"Startup mode: {snapshot['startup_mode']}")
    print(f"Workbench target: {snapshot['workbench_url']}")
    print(f"Workbench state: {snapshot['workbench_status']}")
    if snapshot.get("workbench_error"):
        print(f"Workbench detail: {snapshot['workbench_error']}")
        print("Recovery: check TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL and confirm the Web dev server is running.")
    print(snapshot["service_status_label"])
    if snapshot.get("service_error_hint"):
        print(f"Service detail: {snapshot['service_error_hint']}")
    if snapshot["startup_mode"] == "tray":
        print("Daily entry: shell is resident in tray mode. Use tray Open TraceFold to open the shared workbench.")
    else:
        if snapshot["workbench_status"] == "ready":
            print("Daily entry: shared workbench is open.")
        else:
            print("Daily entry: shell is running, but the shared workbench did not open.")
    if snapshot.get("service_status") in {"unavailable", "invalid_response", "error"}:
        print("Formal records are unaffected by this shell-side status failure.")
    print("Recommended launch: python -m apps.desktop")
    print("Press Ctrl+C to quit.")


def main() -> None:
    app = create_app()
    try:
        snapshot = app.start_runtime()
        _emit_startup_feedback(snapshot)
        app.wait_for_shutdown()
    except KeyboardInterrupt:
        app.quit()
    finally:
        app.close()


if __name__ == "__main__":
    main()
