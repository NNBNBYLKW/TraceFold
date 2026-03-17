from __future__ import annotations

from pydantic import BaseModel, ConfigDict


class PingRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    message: str


class HealthzRead(BaseModel):
    model_config = ConfigDict(extra="forbid")

    status: str
