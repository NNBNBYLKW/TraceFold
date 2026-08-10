from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Literal, Protocol, Sequence

from media_api.storage.contracts import FileSnapshot


@dataclass(frozen=True)
class ProcessorDescriptor:
    name: str
    version: str
    priority: int
    depends_on: tuple[str, ...]
    auto_run_default: bool


@dataclass(frozen=True)
class Detection:
    processor_name: str
    primary: FileSnapshot
    companions: tuple[FileSnapshot, ...]
    expected_output_name: str

    @property
    def inputs(self) -> tuple[FileSnapshot, ...]:
        return (self.primary, *self.companions)


@dataclass(frozen=True)
class ProcessingContext:
    job_id: str
    input_files: tuple[Path, ...]
    job_work_dir: Path


@dataclass(frozen=True)
class ProducedFile:
    path: Path
    actual_filename: str
    media_type: Literal["image", "video"]


class ProcessorExecutionError(RuntimeError):
    def __init__(
        self,
        *,
        error_code: str,
        user_message: str,
        diagnostic_message: str,
        retryable: bool,
    ) -> None:
        self.error_code = error_code
        self.user_message = user_message
        self.diagnostic_message = diagnostic_message
        self.retryable = retryable
        super().__init__(diagnostic_message)


class Processor(Protocol):
    descriptor: ProcessorDescriptor

    def detect(self, sources: Sequence[FileSnapshot]) -> Detection | None: ...

    def process(self, context: ProcessingContext) -> ProducedFile: ...
