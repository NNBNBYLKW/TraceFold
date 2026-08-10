from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from pathlib import Path
from typing import Sequence

from sqlalchemy import select
from sqlalchemy.orm import Session

from media_api.infrastructure.db import utc_now
from media_api.intake.models import InboxItem, InboxStatusEvent
from media_api.intake.repository import InboxRepository
from media_api.intake.state import InboxStatus
from media_api.processing.contracts import Detection, ProcessorDescriptor
from media_api.processing.models import ProcessingJob, ProcessingPlan
from media_api.processing.registry import ProcessorRegistry
from media_api.settings.models import ProcessorSetting
from media_api.storage.contracts import FileSnapshot


class ProcessingPlanError(ValueError):
    pass


class NoApplicableProcessor(ProcessingPlanError):
    pass


class ProcessorDependencyCycle(ProcessingPlanError):
    pass


class MissingProcessorDependency(ProcessingPlanError):
    pass


class DetectionProcessorMismatch(ProcessingPlanError):
    pass


class ProcessingCommandError(ValueError):
    pass


class ProcessingItemNotFound(ProcessingCommandError):
    pass


class ProcessingNotAllowed(ProcessingCommandError):
    pass


class ProcessingIdempotencyKeyReused(ProcessingCommandError):
    pass


class ProcessorDisabled(ProcessingCommandError):
    pass


class AutomaticProcessingNotAllowed(ProcessingCommandError):
    pass


@dataclass(frozen=True)
class PlannedStep:
    processor_name: str
    processor_version: str
    priority: int
    depends_on: tuple[str, ...]


@dataclass(frozen=True)
class PlanDraft:
    steps: tuple[PlannedStep, ...]
    declared_inputs: tuple[FileSnapshot, ...]
    expected_output_name: str
    auto_run_allowed: bool


class ProcessingPlanner:
    def __init__(self, registry: ProcessorRegistry) -> None:
        self.registry = registry

    def plan(self, sources: Sequence[FileSnapshot]) -> PlanDraft:
        source_tuple = tuple(sources)
        detections: dict[str, Detection] = {}
        descriptors: dict[str, ProcessorDescriptor] = {}
        for processor in self.registry.snapshot():
            detection = processor.detect(source_tuple)
            if detection is None:
                continue
            descriptor = processor.descriptor
            if detection.processor_name != descriptor.name:
                raise DetectionProcessorMismatch(
                    f"{descriptor.name} returned detection for {detection.processor_name}"
                )
            detections[descriptor.name] = detection
            descriptors[descriptor.name] = descriptor

        if not detections:
            raise NoApplicableProcessor("no registered processor accepts these sources")

        ordered_names = self._order(descriptors)
        steps = tuple(
            PlannedStep(
                processor_name=name,
                processor_version=descriptors[name].version,
                priority=descriptors[name].priority,
                depends_on=descriptors[name].depends_on,
            )
            for name in ordered_names
        )
        return PlanDraft(
            steps=steps,
            declared_inputs=source_tuple,
            expected_output_name=detections[ordered_names[-1]].expected_output_name,
            auto_run_allowed=all(
                descriptors[name].auto_run_default for name in ordered_names
            ),
        )

    @staticmethod
    def _order(descriptors: dict[str, ProcessorDescriptor]) -> list[str]:
        for descriptor in descriptors.values():
            missing = [
                dependency
                for dependency in descriptor.depends_on
                if dependency not in descriptors
            ]
            if missing:
                raise MissingProcessorDependency(
                    f"{descriptor.name} requires {missing[0]}"
                )

        remaining = {name: set(value.depends_on) for name, value in descriptors.items()}
        ordered: list[str] = []
        while remaining:
            ready = [name for name, dependencies in remaining.items() if not dependencies]
            if not ready:
                raise ProcessorDependencyCycle("processor dependency graph contains a cycle")
            ready.sort(key=lambda name: (descriptors[name].priority, name))
            selected = ready[0]
            ordered.append(selected)
            del remaining[selected]
            for dependencies in remaining.values():
                dependencies.discard(selected)
        return ordered

    @staticmethod
    def persist(session: Session, item: InboxItem, draft: PlanDraft) -> ProcessingPlan:
        item.processing_plan_version += 1
        primary = draft.steps[-1]
        plan = ProcessingPlan(
            inbox_item_id=item.id,
            plan_version=item.processing_plan_version,
            processor_name=primary.processor_name,
            processor_version=primary.processor_version,
            steps_json=[
                {
                    "processor_name": step.processor_name,
                    "processor_version": step.processor_version,
                    "priority": step.priority,
                    "depends_on": list(step.depends_on),
                }
                for step in draft.steps
            ],
            declared_inputs_json=[
                {
                    "path": str(snapshot.path),
                    "size": snapshot.size,
                    "modified_ns": snapshot.modified_ns,
                }
                for snapshot in draft.declared_inputs
            ],
            expected_output_name=draft.expected_output_name,
        )
        session.add(plan)
        session.flush()
        return plan


class ProcessingCommandService:
    def __init__(self, *, session: Session, registry: ProcessorRegistry) -> None:
        self.session = session
        self.registry = registry
        self.items = InboxRepository(session)
        self.planner = ProcessingPlanner(registry)

    def create_job(
        self,
        item_id: str,
        idempotency_key: str,
        *,
        automatic: bool = False,
    ) -> ProcessingJob:
        if not idempotency_key.strip():
            raise ProcessingCommandError("idempotency key must not be empty")
        existing = self.session.scalar(
            select(ProcessingJob).where(
                ProcessingJob.idempotency_key == idempotency_key
            )
        )
        if existing is not None:
            if existing.inbox_item_id != item_id:
                raise ProcessingIdempotencyKeyReused(
                    "idempotency key belongs to a different inbox item"
                )
            return existing

        item = self.items.get(item_id)
        if item is None:
            raise ProcessingItemNotFound(item_id)
        if item.status not in {InboxStatus.DISCOVERED, InboxStatus.READY_TO_TAG}:
            raise ProcessingNotAllowed(item.status)

        snapshots = tuple(
            FileSnapshot(
                path=Path(source.canonical_path),
                size=source.size,
                modified_ns=source.modified_ns,
            )
            for source in sorted(
                item.sources,
                key=lambda source: (not source.is_primary, source.canonical_path),
            )
        )
        draft = self.planner.plan(snapshots)
        self._require_enabled(draft, automatic=automatic)
        plan = self.planner.persist(self.session, item, draft)
        job = ProcessingJob(
            inbox_item_id=item.id,
            processing_plan_id=plan.id,
            idempotency_key=idempotency_key,
        )
        self.session.add(job)
        self._transition_to_processing(item)
        self.session.commit()
        self.session.refresh(job)
        return job

    def _require_enabled(self, draft: PlanDraft, *, automatic: bool) -> None:
        for step in draft.steps:
            setting = self.session.get(ProcessorSetting, step.processor_name)
            if setting is not None and not setting.enabled:
                raise ProcessorDisabled(step.processor_name)
            if automatic and setting is not None and not setting.auto_run_default:
                raise AutomaticProcessingNotAllowed(step.processor_name)
        if automatic and not draft.auto_run_allowed:
            raise AutomaticProcessingNotAllowed(
                "one or more processors require manual confirmation"
            )

    def _transition_to_processing(self, item: InboxItem) -> None:
        previous = item.status
        event_time = utc_now()
        item.status = InboxStatus.ANALYZING
        self.session.add(
            InboxStatusEvent(
                inbox_item_id=item.id,
                from_status=previous,
                to_status=InboxStatus.ANALYZING,
                reason="processing plan created",
                created_at=event_time,
            )
        )
        item.status = InboxStatus.PROCESSING
        self.session.add(
            InboxStatusEvent(
                inbox_item_id=item.id,
                from_status=InboxStatus.ANALYZING,
                to_status=InboxStatus.PROCESSING,
                reason="persistent processing job queued",
                created_at=event_time + timedelta(microseconds=1),
            )
        )
