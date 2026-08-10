from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest

from media_api.processing.contracts import (
    Detection,
    ProcessingContext,
    ProcessorDescriptor,
    ProducedFile,
)
from media_api.processing.planner import (
    ProcessingPlanner,
    ProcessorDependencyCycle,
)
from media_api.processing.registry import ProcessorRegistry
from media_api.storage.contracts import FileSnapshot


SOURCES = (
    FileSnapshot(path=Path("C:/inbox/clip.mp4"), size=12, modified_ns=34),
)


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


def _processor(
    name: str,
    *,
    depends_on: tuple[str, ...] = (),
    priority: int = 10,
    auto_run_default: bool = True,
) -> FakeProcessor:
    return FakeProcessor(
        ProcessorDescriptor(
            name=name,
            version="1",
            priority=priority,
            depends_on=depends_on,
            auto_run_default=auto_run_default,
        )
    )


def test_planner_orders_explicit_dependencies() -> None:
    registry = ProcessorRegistry(
        [
            _processor("cleanup", depends_on=("merge",), priority=5),
            _processor("merge", priority=20),
        ]
    )

    plan = ProcessingPlanner(registry).plan(SOURCES)

    assert [step.processor_name for step in plan.steps] == ["merge", "cleanup"]
    assert plan.declared_inputs == SOURCES
    assert plan.expected_output_name == "clip.mp4"


def test_planner_uses_priority_then_name_for_independent_steps() -> None:
    registry = ProcessorRegistry(
        [
            _processor("zeta", priority=20),
            _processor("beta", priority=10),
            _processor("alpha", priority=10),
        ]
    )

    plan = ProcessingPlanner(registry).plan(SOURCES)

    assert [step.processor_name for step in plan.steps] == [
        "alpha",
        "beta",
        "zeta",
    ]


def test_planner_rejects_dependency_cycle() -> None:
    cyclic_registry = ProcessorRegistry(
        [
            _processor("first", depends_on=("second",)),
            _processor("second", depends_on=("first",)),
        ]
    )

    with pytest.raises(ProcessorDependencyCycle):
        ProcessingPlanner(cyclic_registry).plan(SOURCES)


def test_plan_disables_auto_run_when_any_step_is_not_safe_by_default() -> None:
    registry = ProcessorRegistry(
        [
            _processor("safe"),
            _processor("manual", depends_on=("safe",), auto_run_default=False),
        ]
    )

    plan = ProcessingPlanner(registry).plan(SOURCES)

    assert plan.auto_run_allowed is False
