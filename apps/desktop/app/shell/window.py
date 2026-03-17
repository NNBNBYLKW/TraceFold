from __future__ import annotations

from dataclasses import dataclass
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
    last_opened_url: str | None = None
    load_state: str = "idle"
    last_error: str | None = None
    visible: bool = False
    service_status_label: str = "Service: unknown"
    service_status_hint: str | None = None

    def open_workbench(self, *, url: str | None = None) -> WorkbenchLoadResult:
        target_url = (url or self.workbench_url or "").strip()
        validation_error = self._validate_workbench_url(target_url)
        if validation_error is not None:
            self.load_state = "error"
            self.last_error = validation_error
            return WorkbenchLoadResult(status="error", error=validation_error)

        self.load_state = "loading"
        self.last_opened_url = target_url
        self.last_error = None
        self.visible = True

        # The shell only hosts or opens the existing workbench URL.
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
        label = "Service: available" if status == "ok" else "Service: unavailable"
        self.service_status_label = label
        self.service_status_hint = error_hint
        return {
            "label": self.service_status_label,
            "hint": self.service_status_hint,
        }

    @staticmethod
    def _validate_workbench_url(url: str) -> str | None:
        if not url:
            return "Workbench URL is not configured."

        parsed = urlparse(url)
        if parsed.scheme not in {"http", "https"} or not parsed.netloc:
            return "Workbench URL is invalid."

        return None
