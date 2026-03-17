from dataclasses import dataclass


@dataclass(slots=True)
class TrayMenuItem:
    key: str
    label: str


class TrayIntegrationSkeleton:
    def __init__(self) -> None:
        self.visible = False
        self.last_action: str | None = None

    def show(self) -> None:
        self.visible = True

    def hide(self) -> None:
        self.visible = False

    def menu_items(self, *, window_visible: bool) -> list[TrayMenuItem]:
        toggle_label = "Hide Window" if window_visible else "Show Window"
        return [
            TrayMenuItem(key="open", label="Open TraceFold"),
            TrayMenuItem(key="toggle_window", label=toggle_label),
            TrayMenuItem(key="quit", label="Quit"),
        ]

    def remember_action(self, action: str) -> None:
        self.last_action = action
