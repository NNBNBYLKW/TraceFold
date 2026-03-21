from __future__ import annotations

import webbrowser
from dataclasses import dataclass, field
from typing import Callable
from urllib.parse import urlparse


@dataclass(slots=True)
class WorkbenchLoadResult:
    status: str
    url: str | None = None
    error: str | None = None


@dataclass(slots=True)
class MainWindowSkeleton:
    title: str
    workbench_url: str
    url_launcher: Callable[[str], bool] = field(
        default=webbrowser.open,
        repr=False,
        compare=False,
    )
    last_opened_url: str | None = None
    load_state: str = "idle"
    last_error: str | None = None
    visible: bool = False
    workbench_status_label: str = "Current mode: not set"
    service_status_label: str = "Service status: unknown"
    service_status_hint: str | None = None

    def open_workbench(self, *, url: str | None = None) -> WorkbenchLoadResult:
        target_url = (url or self.workbench_url or "").strip()
        validation_error = self._validate_workbench_url(target_url)
        if validation_error is not None:
            self.load_state = "error"
            self.last_error = validation_error
            return WorkbenchLoadResult(status="error", error=validation_error)

        self.load_state = "loading"
        self.last_error = None
        try:
            launched = bool(self.url_launcher(target_url))
        except Exception:
            launched = False

        if not launched:
            self.load_state = "error"
            self.last_error = "Workbench URL could not be opened."
            self.visible = False
            return WorkbenchLoadResult(status="error", error=self.last_error)

        # The shell only opens the existing workbench URL.
        self.last_opened_url = target_url
        self.visible = True
        self.load_state = "ready"
        return WorkbenchLoadResult(status="ready", url=target_url)

    def show(self) -> None:
        self.visible = True

    def hide(self) -> None:
        self.visible = False

    def update_service_status(
        self,
        *,
        status: str,
        error_hint: str | None = None,
    ) -> dict[str, str | None]:
        if status == "ok":
            label = "Service status: available"
        elif status == "invalid_response":
            label = "Service status: invalid response"
        elif status == "error":
            label = "Service status: failed"
        else:
            label = "Service status: unavailable"
        self.service_status_label = label
        self.service_status_hint = error_hint
        return {
            "label": self.service_status_label,
            "hint": self.service_status_hint,
        }

    def update_workbench_status(
        self,
        *,
        active_mode_name: str | None = None,
    ) -> dict[str, str]:
        label = f"Current mode: {active_mode_name}" if active_mode_name else "Current mode: not set"
        self.workbench_status_label = label
        return {"label": self.workbench_status_label}

    @staticmethod
    def _validate_workbench_url(url: str) -> str | None:
        if not url:
            return "Workbench URL is not configured."

        parsed = urlparse(url)
        if parsed.scheme not in {"http", "https"} or not parsed.netloc:
            return "Workbench URL is invalid."

        return None
