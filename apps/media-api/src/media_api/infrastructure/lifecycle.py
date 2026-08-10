from __future__ import annotations

import asyncio
import logging
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

from fastapi import FastAPI

from media_api.api.dependencies import RuntimeState
from media_api.api.events import EventBus, MediaEvent


logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class RuntimeLoopSettings:
    automatic_scan_enabled: bool
    scan_interval_seconds: float


class RuntimeCoordinator:
    def __init__(
        self,
        *,
        state: RuntimeState,
        event_bus: EventBus,
        recover_ingests: Callable[[], Any],
        recover_processing_jobs: Callable[[], Any],
        run_worker_once: Callable[[], Any],
        scan_once: Callable[[], Any],
        load_loop_settings: Callable[[], RuntimeLoopSettings],
        dispose: Callable[[], Any],
        idle_poll_seconds: float = 0.5,
    ) -> None:
        self.state = state
        self.event_bus = event_bus
        self.recover_ingests = recover_ingests
        self.recover_processing_jobs = recover_processing_jobs
        self.run_worker_once = run_worker_once
        self.scan_once = scan_once
        self.load_loop_settings = load_loop_settings
        self.dispose = dispose
        self.idle_poll_seconds = idle_poll_seconds
        self.lifecycle_events: list[str] = []
        self._stop = asyncio.Event()
        self._tasks: list[asyncio.Task[None]] = []
        self._started = False

    async def start(self) -> None:
        if self._started:
            return
        self._started = True
        self.lifecycle_events.append("disable_ingest")
        self.state.ingest_enabled = False
        self.state.recovery_in_progress = True

        self.lifecycle_events.append("recover_ingests")
        await asyncio.to_thread(self.recover_ingests)
        self.lifecycle_events.append("recover_processing_jobs")
        await asyncio.to_thread(self.recover_processing_jobs)

        self.lifecycle_events.append("start_worker")
        self._tasks.append(asyncio.create_task(self._worker_loop()))
        self.lifecycle_events.append("start_scanner")
        self._tasks.append(asyncio.create_task(self._scanner_loop()))

        self.state.recovery_in_progress = False
        self.state.ingest_enabled = True
        self.lifecycle_events.append("enable_ingest")
        await self.event_bus.publish(
            MediaEvent(type="system.ready", entity_id="system", status="ready")
        )

    async def stop(self) -> None:
        if not self._started:
            return
        self.state.ingest_enabled = False
        self._stop.set()
        await self.event_bus.publish(
            MediaEvent(type="system.stopping", entity_id="system", status="stopping")
        )
        if self._tasks:
            await asyncio.gather(*self._tasks, return_exceptions=True)
        await self.event_bus.close()
        await asyncio.to_thread(self.dispose)
        self.lifecycle_events.append("disposed")
        self._started = False

    @asynccontextmanager
    async def lifespan(self, _app: FastAPI) -> AsyncIterator[None]:
        await self.start()
        try:
            yield
        finally:
            await self.stop()

    async def _worker_loop(self) -> None:
        while not self._stop.is_set():
            try:
                result = await asyncio.to_thread(self.run_worker_once)
                if result is not None:
                    await self.event_bus.publish(
                        MediaEvent(
                            type="processing.updated",
                            entity_id=str(getattr(result, "job_id", "processing")),
                            status=str(getattr(result, "status", "updated")),
                        )
                    )
            except Exception:
                logger.exception("persistent worker loop failed")
            await self._wait_or_stop(self.idle_poll_seconds)

    async def _scanner_loop(self) -> None:
        while not self._stop.is_set():
            settings = await asyncio.to_thread(self.load_loop_settings)
            wait_seconds = self.idle_poll_seconds
            if settings.automatic_scan_enabled:
                try:
                    result = await asyncio.to_thread(self.scan_once)
                    self.state.last_scan_at = datetime.now(timezone.utc)
                    await self.event_bus.publish(
                        MediaEvent(
                            type="inbox.scanned",
                            entity_id="inbox",
                            status=str(getattr(result, "created_items", "completed")),
                        )
                    )
                except Exception:
                    logger.exception("automatic scanner loop failed")
                wait_seconds = settings.scan_interval_seconds
            await self._wait_or_stop(wait_seconds)

    async def _wait_or_stop(self, seconds: float) -> None:
        try:
            await asyncio.wait_for(self._stop.wait(), timeout=seconds)
        except TimeoutError:
            pass
