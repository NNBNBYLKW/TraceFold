from dataclasses import dataclass


@dataclass(slots=True)
class NotificationEvent:
    title: str
    body: str
    level: str = "info"
    action_key: str | None = None


class NotificationBridgeSkeleton:
    def __init__(self) -> None:
        self.events: list[NotificationEvent] = []

    def publish(
        self,
        *,
        title: str,
        body: str,
        level: str = "info",
        action_key: str | None = None,
    ) -> NotificationEvent:
        event = NotificationEvent(
            title=title,
            body=body,
            level=level,
            action_key=action_key,
        )
        self.events.append(event)
        return event
