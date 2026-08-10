from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import select

from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.processing.contracts import (
    Detection,
    ProcessingContext,
    ProcessorDescriptor,
    ProducedFile,
)
from media_api.processing.models import ProcessingJob, ProcessingPlan
from media_api.processing.planner import (
    AutomaticProcessingNotAllowed,
    ProcessingCommandService,
    ProcessorDisabled,
)
from media_api.settings.models import ProcessorSetting


@dataclass
class ApiProcessor:
    descriptor: ProcessorDescriptor = ProcessorDescriptor(
        name="api_processor",
        version="1",
        priority=10,
        depends_on=(),
        auto_run_default=True,
    )

    def detect(self, sources):
        return Detection(
            processor_name=self.descriptor.name,
            primary=sources[0],
            companions=tuple(sources[1:]),
            expected_output_name="processed.mp4",
        )

    def process(self, context: ProcessingContext) -> ProducedFile:
        output = context.job_work_dir / "processed.mp4"
        output.write_bytes(b"processed")
        return ProducedFile(path=output, actual_filename=output.name, media_type="video")


def _create_item(client: TestClient, filename: str) -> str:
    source = Path(client.app.state.settings.inbox_dir) / filename
    source.write_bytes(b"api-source")
    with client.app.state.session_factory() as session:
        item = InboxItem(
            media_type="video",
            status=InboxStatus.READY_TO_TAG,
            suggested_actual_filename=filename,
        )
        item.sources.append(
            SourceFile.from_snapshot(client.app.state.storage.snapshot(source), is_primary=True)
        )
        session.add(item)
        session.commit()
        return item.id


def test_process_command_requires_idempotency_key_and_reuses_job(client: TestClient) -> None:
    client.app.state.processor_registry.register(ApiProcessor())
    item_id = _create_item(client, "process.mp4")

    missing = client.post(f"/api/inbox/items/{item_id}/process")
    first = client.post(
        f"/api/inbox/items/{item_id}/process",
        headers={"Idempotency-Key": "process-once"},
    )
    duplicate = client.post(
        f"/api/inbox/items/{item_id}/process",
        headers={"Idempotency-Key": "process-once"},
    )

    assert missing.status_code == 422
    assert first.status_code == 200
    assert first.json()["status"] == "queued"
    assert duplicate.status_code == 200
    assert duplicate.json()["id"] == first.json()["id"]
    with client.app.state.session_factory() as session:
        job = session.get(ProcessingJob, first.json()["id"])
        assert job is not None
        plan = session.get(ProcessingPlan, job.processing_plan_id)
        assert plan is not None
        assert plan.processor_name == "api_processor"
        assert plan.processor_version == "1"
        assert plan.expected_output_name == "processed.mp4"
        assert plan.steps_json == [
            {
                "processor_name": "api_processor",
                "processor_version": "1",
                "priority": 10,
                "depends_on": [],
            }
        ]
        assert plan.declared_inputs_json[0]["path"].endswith("process.mp4")


def test_idempotency_key_cannot_be_reused_for_another_item(client: TestClient) -> None:
    client.app.state.processor_registry.register(ApiProcessor())
    first_item = _create_item(client, "first-process.mp4")
    second_item = _create_item(client, "second-process.mp4")
    client.post(
        f"/api/inbox/items/{first_item}/process",
        headers={"Idempotency-Key": "shared-process-key"},
    )

    response = client.post(
        f"/api/inbox/items/{second_item}/process",
        headers={"Idempotency-Key": "shared-process-key"},
    )

    assert response.status_code == 409


def test_automatic_processing_requires_safe_descriptor_and_enabled_setting(
    client: TestClient,
) -> None:
    manual = ApiProcessor(
        descriptor=ProcessorDescriptor(
            name="manual_processor",
            version="1",
            priority=10,
            depends_on=(),
            auto_run_default=False,
        )
    )
    client.app.state.processor_registry.register(manual)
    item_id = _create_item(client, "manual-process.mp4")

    with client.app.state.session_factory() as session:
        with pytest.raises(AutomaticProcessingNotAllowed):
            ProcessingCommandService(
                session=session,
                registry=client.app.state.processor_registry,
            ).create_job(item_id, "automatic-manual", automatic=True)

    with client.app.state.session_factory() as session:
        setting = ProcessorSetting(
            processor_name="manual_processor",
            enabled=False,
            auto_run_default=True,
        )
        session.add(setting)
        session.commit()
        with pytest.raises(ProcessorDisabled):
            ProcessingCommandService(
                session=session,
                registry=client.app.state.processor_registry,
            ).create_job(item_id, "disabled-manual", automatic=False)
        assert session.scalar(select(ProcessingJob)) is None
