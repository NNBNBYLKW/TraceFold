from __future__ import annotations

import asyncio
import json
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone

from fastapi import APIRouter
from fastapi.responses import StreamingResponse


@dataclass(frozen=True)
class MediaEvent:
    type: str
    entity_id: str
    status: str | None = None
    timestamp: datetime = field(
        default_factory=lambda: datetime.now(timezone.utc)
    )


_CLOSE = object()


class EventBus:
    def __init__(
        self,
        *,
        queue_size: int = 100,
        heartbeat_seconds: float = 15.0,
    ) -> None:
        if queue_size < 1:
            raise ValueError("queue_size must be positive")
        self.queue_size = queue_size
        self.heartbeat_seconds = heartbeat_seconds
        self._subscribers: set[asyncio.Queue[MediaEvent | object]] = set()
        self._closed = False
        self._loop: asyncio.AbstractEventLoop | None = None
        self.dropped_events = 0

    @property
    def subscriber_count(self) -> int:
        return len(self._subscribers)

    @asynccontextmanager
    async def subscribe(self) -> AsyncIterator[asyncio.Queue[MediaEvent | object]]:
        if self._closed:
            raise RuntimeError("event bus is closed")
        queue: asyncio.Queue[MediaEvent | object] = asyncio.Queue(
            maxsize=self.queue_size
        )
        self._loop = asyncio.get_running_loop()
        self._subscribers.add(queue)
        try:
            yield queue
        finally:
            self._subscribers.discard(queue)

    async def publish(self, event: MediaEvent) -> None:
        self._publish_nowait(event)

    def publish_threadsafe(self, event: MediaEvent) -> None:
        loop = self._loop
        if loop is None or loop.is_closed():
            return
        loop.call_soon_threadsafe(self._publish_nowait, event)

    def _publish_nowait(self, event: MediaEvent) -> None:
        if self._closed:
            return
        for queue in tuple(self._subscribers):
            try:
                queue.put_nowait(event)
            except asyncio.QueueFull:
                self.dropped_events += 1

    async def close(self) -> None:
        self._closed = True
        for queue in tuple(self._subscribers):
            if queue.full():
                try:
                    queue.get_nowait()
                except asyncio.QueueEmpty:
                    pass
            try:
                queue.put_nowait(_CLOSE)
            except asyncio.QueueFull:
                pass

    async def stream(self) -> AsyncIterator[str]:
        async with self.subscribe() as queue:
            while True:
                try:
                    event = await asyncio.wait_for(
                        queue.get(),
                        timeout=self.heartbeat_seconds,
                    )
                except TimeoutError:
                    yield ": heartbeat\n\n"
                    continue
                if event is _CLOSE:
                    return
                assert isinstance(event, MediaEvent)
                payload = asdict(event)
                payload["timestamp"] = event.timestamp.isoformat()
                yield (
                    f"event: {event.type}\n"
                    f"data: {json.dumps(payload, ensure_ascii=False, separators=(',', ':'))}\n\n"
                )


def build_events_router(event_bus: EventBus) -> APIRouter:
    router = APIRouter(prefix="/api", tags=["events"])

    @router.get("/events")
    async def events() -> StreamingResponse:
        return StreamingResponse(
            event_bus.stream(),
            media_type="text/event-stream",
            headers={
                "Cache-Control": "no-cache",
                "X-Accel-Buffering": "no",
            },
        )

    return router
