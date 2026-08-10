from __future__ import annotations

from dataclasses import dataclass

import pytest

from media_api.processing.contracts import (
    Detection,
    ProcessingContext,
    ProcessorDescriptor,
    ProducedFile,
)
from media_api.processing.registry import DuplicateProcessorName, ProcessorRegistry


@dataclass
class FakeProcessor:
    descriptor: ProcessorDescriptor

    def detect(self, sources):
        return Detection(
            processor_name=self.descriptor.name,
            primary=sources[0],
            companions=tuple(sources[1:]),
            expected_output_name=sources[0].path.name,
        )

    def process(self, context: ProcessingContext) -> ProducedFile:
        raise NotImplementedError


def _processor(name: str, version: str) -> FakeProcessor:
    return FakeProcessor(
        ProcessorDescriptor(
            name=name,
            version=version,
            priority=10,
            depends_on=(),
            auto_run_default=True,
        )
    )


def test_registry_rejects_duplicate_names() -> None:
    registry = ProcessorRegistry()
    registry.register(_processor("convert", "1"))

    with pytest.raises(DuplicateProcessorName, match="convert"):
        registry.register(_processor("convert", "2"))


def test_registry_snapshot_is_name_sorted_and_immutable() -> None:
    registry = ProcessorRegistry([_processor("zeta", "1"), _processor("alpha", "1")])

    snapshot = registry.snapshot()
    registry.register(_processor("middle", "1"))

    assert [processor.descriptor.name for processor in snapshot] == ["alpha", "zeta"]
    assert [processor.descriptor.name for processor in registry.snapshot()] == [
        "alpha",
        "middle",
        "zeta",
    ]
