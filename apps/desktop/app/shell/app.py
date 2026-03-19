from __future__ import annotations

from datetime import datetime, timezone

from ..clients.status_client import DesktopStatusClientError, TraceFoldStatusClient
from ..core.config import DesktopShellSettings
from .notifications import NotificationBridgeSkeleton
from .state import DesktopShellState
from .tray import TrayIntegrationSkeleton
from .window import MainWindowSkeleton


class DesktopShellApp:
    def __init__(
        self,
        *,
        settings: DesktopShellSettings,
        status_client: TraceFoldStatusClient,
        window: MainWindowSkeleton | None = None,
        tray: TrayIntegrationSkeleton | None = None,
        notifications: NotificationBridgeSkeleton | None = None,
        state: DesktopShellState | None = None,
    ) -> None:
        self.settings = settings
        self.status_client = status_client
        normalized_workbench_url = self._normalize_workbench_url(settings.web_workbench_url)
        self.window = window or MainWindowSkeleton(
            title="TraceFold Desktop Shell",
            workbench_url=normalized_workbench_url,
        )
        self.window.workbench_url = normalized_workbench_url
        self.tray = tray or TrayIntegrationSkeleton()
        self.notifications = notifications or NotificationBridgeSkeleton()
        self.state = state or DesktopShellState(
            workbench_url=normalized_workbench_url,
            startup_mode=settings.startup_mode,
        )

    def startup_summary(self) -> dict[str, object]:
        return {
            "web_workbench_url": self.state.workbench_url,
            "api_base_url": self.settings.api_base_url,
            "startup_mode": self.settings.startup_mode,
            "debug": self.settings.debug,
            "log_enabled": self.settings.log_enabled,
        }

    def bootstrap(self) -> dict[str, object]:
        self.tray.show()
        self.state.tray_visible = self.tray.visible
        self.state.resident = True
        status_snapshot = self.check_service_status()
        return {
            "service_status": status_snapshot["status"],
            "service_last_checked": status_snapshot["last_checked"],
            "service_error_hint": status_snapshot["error_hint"],
            "tray_visible": self.state.tray_visible,
            "resident": self.state.resident,
        }

    def open_workbench(self, *, url: str | None = None) -> dict[str, str | None]:
        self.tray.remember_action("open")
        result = self.window.open_workbench(url=url)
        self.state.workbench_state = result.status
        self.state.last_workbench_error = result.error
        self.state.active_workbench_url = result.url
        self.state.window_visible = self.window.visible
        return {
            "status": result.status,
            "url": result.url,
            "error": result.error,
        }

    def show_window(self) -> dict[str, object]:
        self.tray.remember_action("show_window")
        self.window.show()
        self.state.window_visible = True
        return {"window_visible": self.state.window_visible}

    def hide_window(self) -> dict[str, object]:
        self.tray.remember_action("hide_window")
        self.window.hide()
        self.state.window_visible = False
        return {"window_visible": self.state.window_visible, "resident": self.state.resident}

    def toggle_window(self) -> dict[str, object]:
        if self.state.window_visible:
            return self.hide_window()
        return self.show_window()

    def tray_menu_items(self) -> list[dict[str, str]]:
        items = self.tray.menu_items(window_visible=self.state.window_visible)
        return [{"key": item.key, "label": item.label} for item in items]

    def check_service_status(self) -> dict[str, str | None]:
        checked_at = datetime.now(timezone.utc).isoformat()
        try:
            payload = self.status_client.get_status()
            status = str(payload.get("status") or "unknown")
            error_hint = None
        except DesktopStatusClientError:
            status = "unavailable"
            error_hint = "Cannot reach TraceFold API."

        self.state.service_status = status
        self.state.service_last_checked = checked_at
        self.state.service_error_hint = error_hint
        self._refresh_workbench_status(status=status)
        window_status = self.window.update_service_status(
            status=status,
            error_hint=error_hint,
        )
        self._maybe_publish_service_notification(status=status, error_hint=error_hint)
        return {
            "status": status,
            "last_checked": checked_at,
            "error_hint": error_hint,
            "label": window_status["label"],
        }

    def quit(self) -> dict[str, object]:
        self.tray.remember_action("quit")
        self.state.quit_requested = True
        self.window.hide()
        self.tray.hide()
        self.state.window_visible = False
        self.state.tray_visible = False
        return {"quit_requested": True}

    def close(self) -> None:
        self.status_client.close()

    def handle_notification_action(self, action_key: str) -> dict[str, str | None]:
        if action_key == "open_workbench":
            return self.open_workbench()
        return {"status": "ignored", "url": None, "error": None}

    def _refresh_workbench_status(self, *, status: str) -> None:
        active_mode_name: str | None = None
        if status == "ok":
            try:
                home_payload = self.status_client.get_workbench_home()
                current_mode = home_payload.get("current_mode")
                if isinstance(current_mode, dict):
                    template_name = current_mode.get("template_name")
                    if isinstance(template_name, str) and template_name.strip():
                        active_mode_name = template_name.strip()
            except DesktopStatusClientError:
                active_mode_name = None

        self.state.active_mode_name = active_mode_name
        workbench_status = self.window.update_workbench_status(active_mode_name=active_mode_name)
        self.state.workbench_status_label = workbench_status["label"]

    def _maybe_publish_service_notification(
        self,
        *,
        status: str,
        error_hint: str | None,
    ) -> None:
        if status == "unavailable":
            if self.state.last_notified_service_status == "unavailable":
                return
            self.notifications.publish(
                title="TraceFold service unavailable",
                body=error_hint or "Cannot reach TraceFold API.",
                level="warning",
                action_key="open_workbench",
            )
            self.state.last_notified_service_status = "unavailable"
            return

        self.state.last_notified_service_status = status

    @staticmethod
    def _normalize_workbench_url(url: str) -> str:
        normalized = (url or "").strip().rstrip("/")
        if not normalized:
            return normalized
        if normalized.endswith("/workbench"):
            return normalized
        if normalized.startswith("http://") or normalized.startswith("https://"):
            if normalized.count("/") <= 2:
                return f"{normalized}/workbench"
        return normalized
