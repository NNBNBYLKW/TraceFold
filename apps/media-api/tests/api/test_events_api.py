from __future__ import annotations

import asyncio
import json

import httpx
import pytest
from fastapi import FastAPI

from media_api.api.events import EventBus, MediaEvent, build_events_router


@pytest.mark.asyncio
async def test_event_stream_emits_item_change() -> None:
    app = FastAPI()
    event_bus = EventBus(queue_size=2, heartbeat_seconds=0.05)
    app.include_router(build_events_router(event_bus))
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport,
        base_url="http://test",
    ) as client:
        response_task = asyncio.create_task(client.get("/api/events"))
        for _ in range(100):
            if event_bus.subscriber_count == 1:
                break
            await asyncio.sleep(0)
        await event_bus.publish(
            MediaEvent(type="inbox.updated", entity_id="item-1", status="ready_to_tag")
        )
        await asyncio.sleep(0)
        await event_bus.close()
        response = await asyncio.wait_for(response_task, timeout=2)

    assert response.status_code == 200
    assert "event: inbox.updated" in response.text
    data_line = next(
        line.removeprefix("data: ")
        for line in response.text.splitlines()
        if line.startswith("data: ")
    )
    payload = json.loads(data_line)
    assert payload["entity_id"] == "item-1"
    assert payload["status"] == "ready_to_tag"
    assert set(payload) == {"type", "entity_id", "status", "timestamp"}


@pytest.mark.asyncio
async def test_slow_subscriber_does_not_block_publishers() -> None:
    event_bus = EventBus(queue_size=1)
    async with event_bus.subscribe() as queue:
        await event_bus.publish(MediaEvent(type="inbox.updated", entity_id="first"))
        await event_bus.publish(MediaEvent(type="inbox.updated", entity_id="dropped"))

        event = queue.get_nowait()

    assert event.entity_id == "first"
    assert event_bus.dropped_events == 1


@pytest.mark.asyncio
async def test_sync_command_can_publish_to_async_subscriber() -> None:
    event_bus = EventBus()
    async with event_bus.subscribe() as queue:
        await asyncio.to_thread(
            event_bus.publish_threadsafe,
            MediaEvent(type="library.updated", entity_id="asset-1"),
        )
        event = await asyncio.wait_for(queue.get(), timeout=1)

    assert isinstance(event, MediaEvent)
    assert event.type == "library.updated"
    assert event.entity_id == "asset-1"
