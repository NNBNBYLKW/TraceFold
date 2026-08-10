from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest
from sqlalchemy.orm import Session, sessionmaker

from media_api.catalog.ingest import IngestService, SourceChanged
from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.intake.service import ScanService
from media_api.processing.contracts import ProcessingContext, ProcessorExecutionError
from media_api.processing.models import ProcessingJob
from media_api.processing.processors.mp4_m4a import Mp4M4aMergeProcessor
from media_api.processing.registry import ProcessorRegistry
from media_api.processing.worker import PersistentWorker
from media_api.storage.local import LocalStorage


def _stream_types(path: Path) -> set[str]:
    completed = subprocess.run(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_streams",
            "-of",
            "json",
            str(path),
        ],
        shell=False,
        capture_output=True,
        text=True,
        check=True,
        timeout=10,
    )
    return {
        stream["codec_type"]
        for stream in json.loads(completed.stdout)["streams"]
    }


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


def test_processor_merges_real_audio_and_video(
    tmp_path: Path,
    copy_media_fixture,
) -> None:
    if shutil.which("ffmpeg") is None or shutil.which("ffprobe") is None:
        pytest.skip("FFmpeg and FFprobe are required for real-media merge validation")
    video = copy_media_fixture("paired.mp4", tmp_path)
    audio = copy_media_fixture("paired.m4a", tmp_path)
    work_dir = tmp_path / "job"
    work_dir.mkdir()
    processor = Mp4M4aMergeProcessor(timeout_seconds=10)

    output = processor.process(
        ProcessingContext(
            job_id="real-merge",
            input_files=(video, audio),
            job_work_dir=work_dir,
        )
    )

    assert output.path.name == video.name
    assert _stream_types(output.path) == {"audio", "video"}
    assert video.exists()
    assert audio.exists()


def test_missing_ffmpeg_has_stable_error_code(tmp_path: Path) -> None:
    video = tmp_path / "missing.mp4"
    audio = tmp_path / "missing.m4a"
    video.write_bytes(b"video")
    audio.write_bytes(b"audio")
    job_dir = tmp_path / "job"
    job_dir.mkdir()

    with pytest.raises(ProcessorExecutionError) as error:
        Mp4M4aMergeProcessor(ffmpeg_path="definitely-not-ffmpeg").process(
            ProcessingContext(
                job_id="missing-tool",
                input_files=(video, audio),
                job_work_dir=job_dir,
            )
        )

    assert error.value.error_code == "FFMPEG_NOT_FOUND"
    assert error.value.retryable is False


def test_merged_output_has_audio_video_and_sources_delete_after_ingest(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
    copy_media_fixture,
) -> None:
    if shutil.which("ffmpeg") is None or shutil.which("ffprobe") is None:
        pytest.skip("FFmpeg and FFprobe are required for end-to-end merge validation")
    video = copy_media_fixture("paired.mp4", storage.inbox_dir)
    audio = copy_media_fixture("paired.m4a", storage.inbox_dir)
    registry = ProcessorRegistry([Mp4M4aMergeProcessor(timeout_seconds=10)])
    with session_factory() as session:
        scanner = ScanService(session=session, storage=storage, registry=registry)
        scanner.scan()
        scanner.scan()
        item = scanner.list_items()[0]
        item_id = item.id
        assert item.status == "processing"
        assert {Path(source.canonical_path) for source in item.sources} == {video, audio}

    worker = PersistentWorker(
        session_factory=session_factory,
        registry=registry,
        work_dir=storage.work_dir,
    )
    result = worker.run_once()
    assert result is not None
    assert result.status == "completed"

    with session_factory() as session:
        item = ScanService(session=session, storage=storage, registry=registry).get_item(item_id)
        assert item is not None
        job = session.query(ProcessingJob).filter_by(inbox_item_id=item_id).one()
        processing_output = Path(job.output_path or "")
        assert _stream_types(processing_output) == {"audio", "video"}
        ingest_result = IngestService(session=session, storage=storage).ingest(
            item_id, "merge-ingest"
        )

        assert ingest_result.kind == "completed"
        assert ingest_result.final_path is not None
        assert ingest_result.final_path.name == video.name
        assert _stream_types(ingest_result.final_path) == {"audio", "video"}
        assert not video.exists()
        assert not audio.exists()
        assert not processing_output.exists()


def test_ingest_rejects_processing_output_changed_after_validation(
    session_factory: sessionmaker[Session],
    storage: LocalStorage,
    copy_media_fixture,
) -> None:
    if shutil.which("ffmpeg") is None or shutil.which("ffprobe") is None:
        pytest.skip("FFmpeg and FFprobe are required for output-integrity validation")
    video = copy_media_fixture("paired.mp4", storage.inbox_dir)
    audio = copy_media_fixture("paired.m4a", storage.inbox_dir)
    registry = ProcessorRegistry([Mp4M4aMergeProcessor(timeout_seconds=10)])
    with session_factory() as session:
        scanner = ScanService(session=session, storage=storage, registry=registry)
        scanner.scan()
        scanner.scan()
        item_id = scanner.list_items()[0].id
    PersistentWorker(
        session_factory=session_factory,
        registry=registry,
        work_dir=storage.work_dir,
    ).run_once()

    with session_factory() as session:
        job = session.query(ProcessingJob).filter_by(inbox_item_id=item_id).one()
        output = Path(job.output_path or "")
        output.write_bytes(b"changed-after-validation")

        with pytest.raises(SourceChanged):
            IngestService(session=session, storage=storage).prepare(
                item_id, "changed-processing-output"
            )

        assert video.exists()
        assert audio.exists()
        assert output.exists()
        assert not (storage.library_dir / video.name).exists()
