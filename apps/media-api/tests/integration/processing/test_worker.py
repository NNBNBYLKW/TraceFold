from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest
from sqlalchemy.orm import Session, sessionmaker

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.processing.contracts import (
    Detection,
    ProcessingContext,
    ProcessorDescriptor,
    ProcessorExecutionError,
    ProducedFile,
)
from media_api.processing.models import ProcessingJob, ProcessingJobStatus, ProcessingPlan
from media_api.processing.registry import ProcessorRegistry
from media_api.processing.worker import PersistentWorker
from media_api.storage.local import LocalStorage


@dataclass
class FakeProcessor:
    descriptor: ProcessorDescriptor
    behavior: str

    def detect(self, sources):
        return Detection(
            processor_name=self.descriptor.name,
            primary=sources[0],
            companions=tuple(sources[1:]),
            expected_output_name=f"{self.descriptor.name}.bin",
        )

    def process(self, context: ProcessingContext) -> ProducedFile:
        if self.behavior == "failure":
            raise ProcessorExecutionError(
                error_code="FAKE_FAILURE",
                user_message="Fake processing failed",
                diagnostic_message="deterministic failure",
                retryable=True,
            )
        if self.behavior == "escape":
            output = context.job_work_dir.parent / "escaped.bin"
        else:
            output = context.job_work_dir / f"{self.descriptor.name}.bin"
        output.write_bytes(b"processed")
        return ProducedFile(
            path=output,
            actual_filename=output.name,
            media_type="video",
        )


def _processor(name: str, behavior: str) -> FakeProcessor:
    return FakeProcessor(
        descriptor=ProcessorDescriptor(
            name=name,
            version="1",
            priority=10,
            depends_on=(),
            auto_run_default=True,
        ),
        behavior=behavior,
    )


@pytest.fixture
def session_factory(settings: BootstrapSettings):
    factory = create_engine_and_session(settings)
    engine = factory.kw["bind"]
    Base.metadata.create_all(engine)
    try:
        yield factory
    finally:
        Base.metadata.drop_all(engine)
        engine.dispose()


def _queued_job(
    session: Session,
    storage: LocalStorage,
    processor_name: str,
) -> tuple[str, str, Path]:
    source = storage.inbox_dir / f"{processor_name}.mp4"
    source.write_bytes(f"source-{processor_name}".encode())
    item = InboxItem(
        media_type="video",
        status=InboxStatus.PROCESSING,
        suggested_actual_filename=source.name,
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    session.add(item)
    session.flush()
    plan = ProcessingPlan(
        inbox_item_id=item.id,
        plan_version=1,
        processor_name=processor_name,
        processor_version="1",
        steps_json=[{"processor_name": processor_name, "processor_version": "1"}],
        declared_inputs_json=[
            {
                "path": str(source.resolve()),
                "size": source.stat().st_size,
                "modified_ns": source.stat().st_mtime_ns,
            }
        ],
        expected_output_name=f"{processor_name}.bin",
    )
    session.add(plan)
    session.flush()
    job = ProcessingJob(
        inbox_item_id=item.id,
        processing_plan_id=plan.id,
        idempotency_key=f"job-{processor_name}",
    )
    session.add(job)
    session.commit()
    return job.id, item.id, source


def test_worker_marks_only_failed_item_and_continues(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
) -> None:
    with session_factory() as session:
        success_job_id, success_item_id, _ = _queued_job(session, storage, "success")
        failure_job_id, failure_item_id, _ = _queued_job(session, storage, "failure")
    worker = PersistentWorker(
        session_factory=session_factory,
        registry=ProcessorRegistry(
            [_processor("success", "success"), _processor("failure", "failure")]
        ),
        work_dir=storage.work_dir,
    )

    worker.run_once()
    worker.run_once()

    with session_factory() as session:
        success_job = session.get(ProcessingJob, success_job_id)
        failure_job = session.get(ProcessingJob, failure_job_id)
        success_item = session.get(InboxItem, success_item_id)
        failure_item = session.get(InboxItem, failure_item_id)
        assert success_job is not None
        assert failure_job is not None
        assert success_item is not None
        assert failure_item is not None
        assert success_job.status is ProcessingJobStatus.COMPLETED
        assert success_job.output_size == len(b"processed")
        assert success_job.output_modified_ns is not None
        assert success_job.output_sha256 is not None
        assert success_item.status is InboxStatus.READY_TO_TAG
        assert failure_job.status is ProcessingJobStatus.FAILED
        assert failure_job.retryable is True
        assert failure_job.error_code == "FAKE_FAILURE"
        assert failure_item.status is InboxStatus.FAILED
        assert failure_item.error_code == "FAKE_FAILURE"


def test_worker_rejects_output_outside_job_work_dir(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
) -> None:
    with session_factory() as session:
        job_id, _, _ = _queued_job(session, storage, "escape")
    worker = PersistentWorker(
        session_factory=session_factory,
        registry=ProcessorRegistry([_processor("escape", "escape")]),
        work_dir=storage.work_dir,
    )

    result = worker.run_once()

    assert result is not None
    assert result.job_id == job_id
    assert result.error_code == "PROCESSOR_OUTPUT_OUTSIDE_WORK_DIR"
    assert (storage.jobs_dir / "escaped.bin").exists()


def test_startup_requeues_retryable_running_job_and_preserves_source(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
) -> None:
    with session_factory() as session:
        job_id, _, source = _queued_job(session, storage, "recover")
        job = session.get(ProcessingJob, job_id)
        assert job is not None
        job.status = ProcessingJobStatus.RUNNING
        session.commit()
    job_dir = storage.jobs_dir / job_id
    job_dir.mkdir()
    (job_dir / "partial.bin").write_bytes(b"partial")

    recovered = PersistentWorker(
        session_factory=session_factory,
        registry=ProcessorRegistry([_processor("recover", "success")]),
        work_dir=storage.work_dir,
    ).recover_interrupted()

    with session_factory() as session:
        job = session.get(ProcessingJob, job_id)
        assert job is not None
        assert recovered == 1
        assert job.status is ProcessingJobStatus.QUEUED
        assert job.attempt_count == 1
        assert not job_dir.exists()
        assert source.exists()
