from dataclasses import dataclass


@dataclass(slots=True)
class DesktopShellState:
    workbench_url: str
    startup_mode: str
    service_status: str = "unknown"
    service_last_checked: str | None = None
    service_error_hint: str | None = None
    last_notified_service_status: str | None = None
    workbench_state: str = "idle"
    last_workbench_error: str | None = None
    active_workbench_url: str | None = None
    tray_visible: bool = False
    window_visible: bool = False
    resident: bool = True
    quit_requested: bool = False
    notifications_enabled: bool = True
