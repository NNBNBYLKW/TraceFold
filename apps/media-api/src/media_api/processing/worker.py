from __future__ import annotations

import shutil
from dataclasses import dataclass
from pathlib import Path

from sqlalchemy import select, update
from sqlalchemy.orm import Session, sessionmaker

from media_api.infrastructure.db import utc_now
from media_api.intake.models import InboxItem, InboxStatusEvent
from media_api.intake.state import InboxStatus
from media_api.processing.contracts import (
    ProcessingContext,
    ProcessorExecutionError,
    ProducedFile,
)
from media_api.processing.models import ProcessingJob, ProcessingJobStatus
from media_api.processing.registry import ProcessorNotRegistered, ProcessorRegistry


@dataclass(frozen=True)
class WorkerResult:
    job_id: str
    status: str
    error_code: str | None = None


class PersistentWorker:
    def __init__(
        self,
        *,
        session_factory: sessionmaker[Session],
        registry: ProcessorRegistry,
        work_dir: Path,
    ) -> None:
        self.session_factory = session_factory
        self.registry = registry
        self.jobs_dir = work_dir.resolve() / "jobs"
        self.jobs_dir.mkdir(parents=True, exist_ok=True)

    def run_once(self) -> WorkerResult | None:
        job_id = self._claim_one()
        if job_id is None:
            return None
        return self.execute(job_id)

    def execute(self, job: str | ProcessingJob) -> WorkerResult:
        job_id = job.id if isinstance(job, ProcessingJob) else job
        with self.session_factory() as session:
            persisted = session.get(ProcessingJob, job_id)
            if persisted is None:
                raise LookupError(f"processing job not found: {job_id}")
            try:
                output = self._execute_steps(session, persisted)
            except ProcessorExecutionError as exc:
                self._fail_job(session, persisted, exc)
                return WorkerResult(
                    job_id=persisted.id,
                    status=ProcessingJobStatus.FAILED.value,
                    error_code=exc.error_code,
                )
            except Exception as exc:
                failure = ProcessorExecutionError(
                    error_code="PROCESSOR_UNEXPECTED_FAILURE",
                    user_message="Processing failed unexpectedly",
                    diagnostic_message=str(exc),
                    retryable=True,
                )
                self._fail_job(session, persisted, failure)
                return WorkerResult(
                    job_id=persisted.id,
                    status=ProcessingJobStatus.FAILED.value,
                    error_code=failure.error_code,
                )

            persisted.status = ProcessingJobStatus.COMPLETED
            persisted.output_path = str(output.path)
            persisted.finished_at = utc_now()
            persisted.error_code = None
            persisted.user_message = None
            persisted.diagnostic_message = None
            item = session.get(InboxItem, persisted.inbox_item_id)
            if item is None:
                raise LookupError("processing job inbox item is missing")
            previous = item.status
            item.status = InboxStatus.READY_TO_TAG
            item.failed_from_status = None
            item.error_code = None
            item.error_message = None
            session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=previous,
                    to_status=InboxStatus.READY_TO_TAG,
                    reason="processing output validated",
                )
            )
            session.commit()
            return WorkerResult(
                job_id=persisted.id,
                status=ProcessingJobStatus.COMPLETED.value,
            )

    def recover_interrupted(self) -> int:
        with self.session_factory() as session:
            jobs = list(
                session.scalars(
                    select(ProcessingJob).where(
                        ProcessingJob.status == ProcessingJobStatus.RUNNING
                    )
                )
            )
            for job in jobs:
                self._delete_owned_job_dir(job.id)
                job.output_path = None
                job.current_step = 0
                job.started_at = None
                job.finished_at = None
                if job.retryable:
                    job.status = ProcessingJobStatus.QUEUED
                    job.attempt_count += 1
                else:
                    job.status = ProcessingJobStatus.FAILED
                    job.finished_at = utc_now()
                    failure = ProcessorExecutionError(
                        error_code="INTERRUPTED_NON_RETRYABLE_JOB",
                        user_message="Processing was interrupted and cannot be retried",
                        diagnostic_message="non-retryable job was running at application startup",
                        retryable=False,
                    )
                    self._apply_failure(session, job, failure)
            session.commit()
            return len(jobs)

    def _claim_one(self) -> str | None:
        with self.session_factory() as session:
            job_id = session.scalar(
                select(ProcessingJob.id)
                .where(ProcessingJob.status == ProcessingJobStatus.QUEUED)
                .order_by(ProcessingJob.created_at, ProcessingJob.id)
                .limit(1)
            )
            if job_id is None:
                return None
            claimed = session.execute(
                update(ProcessingJob)
                .where(
                    ProcessingJob.id == job_id,
                    ProcessingJob.status == ProcessingJobStatus.QUEUED,
                )
                .values(
                    status=ProcessingJobStatus.RUNNING,
                    started_at=utc_now(),
                )
            )
            if claimed.rowcount != 1:
                session.rollback()
                return None
            session.commit()
            return job_id

    def _execute_steps(
        self,
        session: Session,
        job: ProcessingJob,
    ) -> ProducedFile:
        plan = job.processing_plan
        job_dir = self._owned_job_dir(job.id)
        job_dir.mkdir(parents=True, exist_ok=True)
        inputs = tuple(Path(value["path"]) for value in plan.declared_inputs_json)
        output: ProducedFile | None = None
        for index, raw_step in enumerate(plan.steps_json):
            processor_name = str(raw_step["processor_name"])
            processor_version = str(raw_step["processor_version"])
            try:
                processor = self.registry.require(processor_name)
            except ProcessorNotRegistered as exc:
                raise ProcessorExecutionError(
                    error_code="PROCESSOR_NOT_REGISTERED",
                    user_message="The required processor is unavailable",
                    diagnostic_message=processor_name,
                    retryable=False,
                ) from exc
            if processor.descriptor.version != processor_version:
                raise ProcessorExecutionError(
                    error_code="PROCESSOR_VERSION_UNAVAILABLE",
                    user_message="The required processor version is unavailable",
                    diagnostic_message=(
                        f"planned {processor_name} {processor_version}, "
                        f"registered {processor.descriptor.version}"
                    ),
                    retryable=False,
                )
            context = ProcessingContext(
                job_id=job.id,
                input_files=inputs,
                job_work_dir=job_dir,
            )
            output = processor.process(context)
            self._require_owned_output(job_dir, output.path)
            inputs = (output.path,)
            job.current_step = index + 1
            job.output_path = str(output.path)
            session.commit()

        if output is None:
            raise ProcessorExecutionError(
                error_code="EMPTY_PROCESSING_PLAN",
                user_message="Processing plan has no executable steps",
                diagnostic_message=job.processing_plan_id,
                retryable=False,
            )
        return output

    def _fail_job(
        self,
        session: Session,
        job: ProcessingJob,
        failure: ProcessorExecutionError,
    ) -> None:
        self._apply_failure(session, job, failure)
        session.commit()

    @staticmethod
    def _apply_failure(
        session: Session,
        job: ProcessingJob,
        failure: ProcessorExecutionError,
    ) -> None:
        job.status = ProcessingJobStatus.FAILED
        job.error_code = failure.error_code
        job.user_message = failure.user_message
        job.diagnostic_message = failure.diagnostic_message
        job.retryable = failure.retryable
        job.finished_at = utc_now()
        item = session.get(InboxItem, job.inbox_item_id)
        if item is not None:
            previous = item.status
            item.failed_from_status = previous
            item.status = InboxStatus.FAILED
            item.error_code = failure.error_code
            item.error_message = failure.user_message
            session.add(
                InboxStatusEvent(
                    inbox_item_id=item.id,
                    from_status=previous,
                    to_status=InboxStatus.FAILED,
                    reason=failure.user_message,
                )
            )

    def _owned_job_dir(self, job_id: str) -> Path:
        job_dir = (self.jobs_dir / job_id).resolve()
        if job_dir.parent != self.jobs_dir:
            raise ProcessorExecutionError(
                error_code="UNSAFE_PROCESSING_JOB_PATH",
                user_message="Processing job path is unsafe",
                diagnostic_message=job_id,
                retryable=False,
            )
        return job_dir

    def _delete_owned_job_dir(self, job_id: str) -> None:
        job_dir = self._owned_job_dir(job_id)
        if job_dir.exists():
            shutil.rmtree(job_dir)

    @staticmethod
    def _require_owned_output(job_dir: Path, output_path: Path) -> None:
        resolved = output_path.resolve()
        try:
            resolved.relative_to(job_dir)
        except ValueError as exc:
            raise ProcessorExecutionError(
                error_code="PROCESSOR_OUTPUT_OUTSIDE_WORK_DIR",
                user_message="Processor returned an unsafe output path",
                diagnostic_message=str(output_path),
                retryable=False,
            ) from exc
        if not resolved.is_file():
            raise ProcessorExecutionError(
                error_code="PROCESSOR_OUTPUT_MISSING",
                user_message="Processor did not produce its declared output",
                diagnostic_message=str(output_path),
                retryable=True,
            )
