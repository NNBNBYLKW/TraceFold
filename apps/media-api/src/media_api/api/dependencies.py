from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime


@dataclass
class RuntimeState:
    ingest_enabled: bool = True
    recovery_in_progress: bool = False
    last_scan_at: datetime | None = None
