from __future__ import annotations

from datetime import datetime, timezone
from threading import Event

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
        self._shutdown_event = Event()
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
        self._sync_shell_presence()
        status_snapshot = self.check_service_status()
        return {
            "service_status": status_snapshot["status"],
            "service_last_checked": status_snapshot["last_checked"],
            "service_error_hint": status_snapshot["error_hint"],
            "tray_visible": self.state.tray_visible,
            "resident": self.state.resident,
        }

    def start_runtime(self) -> dict[str, object]:
        if self.state.runtime_started and not self.state.quit_requested:
            self._sync_shell_presence()
            return self._runtime_snapshot()

        bootstrap_snapshot = self.bootstrap()
        self.state.quit_requested = False
        self.state.runtime_started = True

        workbench_snapshot = {
            "status": self.state.workbench_state,
            "url": self.state.active_workbench_url,
            "error": self.state.last_workbench_error,
        }
        if self.settings.startup_mode == "window":
            workbench_snapshot = self.open_workbench()
        else:
            self.window.hide()
            self._sync_shell_presence()

        return self._runtime_snapshot(
            service_status=bootstrap_snapshot["service_status"],
            service_last_checked=bootstrap_snapshot["service_last_checked"],
            service_error_hint=bootstrap_snapshot["service_error_hint"],
            workbench_status=workbench_snapshot["status"],
            workbench_error=workbench_snapshot["error"],
        )

    def wait_for_shutdown(
        self,
        *,
        poll_interval_seconds: float = 0.5,
        max_wait_cycles: int | None = None,
    ) -> None:
        wait_cycles = 0
        while not self.state.quit_requested:
            if self._shutdown_event.wait(timeout=poll_interval_seconds):
                break
            wait_cycles += 1
            if max_wait_cycles is not None and wait_cycles >= max_wait_cycles:
                break

    def open_workbench(self, *, url: str | None = None) -> dict[str, str | None]:
        self.tray.remember_shell_action("open_workbench")
        result = self.window.open_workbench(url=url)
        self.state.workbench_state = result.status
        self.state.last_workbench_error = result.error
        self.state.active_workbench_url = result.url
        self._sync_shell_presence()
        return {
            "status": result.status,
            "url": result.url,
            "error": result.error,
        }

    def show_window(self) -> dict[str, object]:
        self.tray.remember_shell_action("show_window")
        if self.state.workbench_state != "ready" or not self.state.active_workbench_url:
            workbench_snapshot = self.open_workbench()
            return {
                "window_visible": self.state.window_visible,
                "workbench_status": workbench_snapshot["status"],
                "workbench_error": workbench_snapshot["error"],
            }
        self.window.show()
        self._sync_shell_presence()
        return {
            "window_visible": self.state.window_visible,
            "workbench_status": self.state.workbench_state,
            "workbench_error": self.state.last_workbench_error,
        }

    def hide_window(self) -> dict[str, object]:
        self.tray.remember_shell_action("hide_window")
        self.window.hide()
        self._sync_shell_presence()
        return {
            "window_visible": self.state.window_visible,
            "resident": self.state.resident,
            "workbench_status": self.state.workbench_state,
        }

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
        except DesktopStatusClientError as exc:
            status, error_hint = self._classify_service_failure(exc)

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
        self.tray.remember_shell_action("quit")
        self.state.quit_requested = True
        self._stop_runtime()
        self._shutdown_event.set()
        return {
            "quit_requested": True,
            "runtime_started": self.state.runtime_started,
            "resident": self.state.resident,
        }

    def close(self) -> None:
        self._stop_runtime()
        self._shutdown_event.set()
        self.status_client.close()

    def handle_notification_action(self, action_key: str) -> dict[str, str | None]:
        if action_key == "open_workbench":
            return self.open_workbench()
        return {"status": "ignored", "url": None, "error": None}

    def handle_tray_action(self, action_key: str) -> dict[str, object]:
        self.tray.remember_menu_action(action_key)
        if action_key == "open":
            opened = self.open_workbench()
            return self._tray_action_snapshot(
                menu_action=action_key,
                shell_action=self.tray.last_shell_action,
                workbench_status=opened["status"],
                workbench_error=opened["error"],
            )
        if action_key == "toggle_window":
            toggled = self.toggle_window()
            return self._tray_action_snapshot(
                menu_action=action_key,
                shell_action=self.tray.last_shell_action,
                workbench_status=toggled.get("workbench_status"),
                workbench_error=toggled.get("workbench_error"),
            )
        if action_key == "quit":
            self.quit()
            return self._tray_action_snapshot(
                menu_action=action_key,
                shell_action=self.tray.last_shell_action,
            )
        return self._tray_action_snapshot(menu_action=action_key, shell_action="ignored")

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

    def _runtime_snapshot(
        self,
        *,
        service_status: str | None = None,
        service_last_checked: str | None = None,
        service_error_hint: str | None = None,
        workbench_status: str | None = None,
        workbench_error: str | None = None,
    ) -> dict[str, object]:
        self._sync_shell_presence()
        return {
            "startup_mode": self.settings.startup_mode,
            "workbench_url": self.state.workbench_url,
            "service_status": service_status or self.state.service_status,
            "service_last_checked": service_last_checked or self.state.service_last_checked,
            "service_error_hint": service_error_hint if service_error_hint is not None else self.state.service_error_hint,
            "service_status_label": self.window.service_status_label,
            "workbench_status": workbench_status or self.state.workbench_state,
            "workbench_error": workbench_error if workbench_error is not None else self.state.last_workbench_error,
            "workbench_status_label": self.window.workbench_status_label,
            "window_visible": self.state.window_visible,
            "tray_visible": self.state.tray_visible,
            "resident": self.state.resident,
            "runtime_started": self.state.runtime_started,
            "quit_requested": self.state.quit_requested,
        }

    def _tray_action_snapshot(
        self,
        *,
        menu_action: str,
        shell_action: str | None,
        workbench_status: str | None = None,
        workbench_error: str | None = None,
    ) -> dict[str, object]:
        self._sync_shell_presence()
        return {
            "menu_action": menu_action,
            "shell_action": shell_action,
            "tray_visible": self.state.tray_visible,
            "window_visible": self.state.window_visible,
            "resident": self.state.resident,
            "runtime_started": self.state.runtime_started,
            "quit_requested": self.state.quit_requested,
            "workbench_status": workbench_status or self.state.workbench_state,
            "workbench_error": workbench_error if workbench_error is not None else self.state.last_workbench_error,
        }

    def _sync_shell_presence(self) -> None:
        self.state.window_visible = self.window.visible
        self.state.tray_visible = self.tray.visible
        self.state.resident = self.tray.visible

    def _stop_runtime(self) -> None:
        self.window.hide()
        self.tray.hide()
        self.state.runtime_started = False
        self._sync_shell_presence()

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
    def _classify_service_failure(exc: DesktopStatusClientError) -> tuple[str, str]:
        message = str(exc).lower()
        if "invalid response" in message:
            return (
                "invalid_response",
                "TraceFold API returned an invalid response. Check /api/healthz and the API process.",
            )
        if "unavailable" in message:
            return (
                "unavailable",
                "Cannot reach TraceFold API. Check /api/healthz and TRACEFOLD_DESKTOP_API_BASE_URL.",
            )
        return (
            "error",
            "TraceFold API status check failed. Check /api/healthz and the API process.",
        )

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
