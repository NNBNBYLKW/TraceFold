from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Sequence

from media_api.processing.contracts import (
    Detection,
    ProcessingContext,
    ProcessorDescriptor,
    ProcessorExecutionError,
    ProducedFile,
)
from media_api.storage.contracts import FileSnapshot


class Mp4M4aMergeProcessor:
    descriptor = ProcessorDescriptor(
        name="mp4_m4a_merge",
        version="1",
        priority=10,
        depends_on=(),
        auto_run_default=True,
    )

    def __init__(
        self,
        *,
        ffmpeg_path: str = "ffmpeg",
        ffprobe_path: str = "ffprobe",
        timeout_seconds: float = 60,
    ) -> None:
        self.ffmpeg_path = ffmpeg_path
        self.ffprobe_path = ffprobe_path
        self.timeout_seconds = timeout_seconds

    def detect(self, sources: Sequence[FileSnapshot]) -> Detection | None:
        groups: dict[tuple[str, str], dict[str, list[FileSnapshot]]] = {}
        for snapshot in sorted(sources, key=lambda value: str(value.path).casefold()):
            extension = snapshot.path.suffix.casefold()
            if extension not in {".mp4", ".m4a"}:
                continue
            key = (str(snapshot.path.parent.resolve()).casefold(), snapshot.path.stem.casefold())
            groups.setdefault(key, {}).setdefault(extension, []).append(snapshot)

        paired_keys = sorted(
            key
            for key, extensions in groups.items()
            if extensions.get(".mp4") and extensions.get(".m4a")
        )
        if not paired_keys:
            return None
        pair = groups[paired_keys[0]]
        primary = pair[".mp4"][0]
        companion = pair[".m4a"][0]
        return Detection(
            processor_name=self.descriptor.name,
            primary=primary,
            companions=(companion,),
            expected_output_name=primary.path.name,
            output_media_type="video",
        )

    def process(self, context: ProcessingContext) -> ProducedFile:
        detection = self.detect(
            [
                FileSnapshot(path=path.resolve(), size=0, modified_ns=0)
                for path in context.input_files
            ]
        )
        if detection is None:
            raise ProcessorExecutionError(
                error_code="MP4_M4A_INPUT_PAIR_REQUIRED",
                user_message="A matching MP4 and M4A pair is required",
                diagnostic_message=", ".join(str(path) for path in context.input_files),
                retryable=False,
            )

        video_path = detection.primary.path
        audio_path = detection.companions[0].path
        output_path = context.job_work_dir / video_path.name
        command = [
            self.ffmpeg_path,
            "-y",
            "-i",
            str(video_path),
            "-i",
            str(audio_path),
            "-map",
            "0:v:0",
            "-map",
            "1:a:0",
            "-c",
            "copy",
            str(output_path),
        ]
        completed = self._run_ffmpeg(command)
        if completed.returncode != 0:
            raise ProcessorExecutionError(
                error_code="FFMPEG_INVALID_INPUT",
                user_message="The video and audio files could not be merged",
                diagnostic_message=_diagnostic(completed.stderr),
                retryable=False,
            )
        self._validate_output(output_path)
        return ProducedFile(
            path=output_path,
            actual_filename=video_path.name,
            media_type="video",
        )

    def _run_ffmpeg(self, command: list[str]) -> subprocess.CompletedProcess[str]:
        try:
            return subprocess.run(
                command,
                shell=False,
                capture_output=True,
                text=True,
                check=False,
                timeout=self.timeout_seconds,
            )
        except FileNotFoundError as exc:
            raise ProcessorExecutionError(
                error_code="FFMPEG_NOT_FOUND",
                user_message="FFmpeg is not installed or configured",
                diagnostic_message=str(exc),
                retryable=False,
            ) from exc
        except subprocess.TimeoutExpired as exc:
            raise ProcessorExecutionError(
                error_code="FFMPEG_TIMEOUT",
                user_message="Merging the video and audio timed out",
                diagnostic_message=str(exc),
                retryable=True,
            ) from exc

    def _validate_output(self, output_path: Path) -> None:
        command = [
            self.ffprobe_path,
            "-v",
            "error",
            "-show_streams",
            "-of",
            "json",
            str(output_path),
        ]
        try:
            completed = subprocess.run(
                command,
                shell=False,
                capture_output=True,
                text=True,
                check=False,
                timeout=self.timeout_seconds,
            )
        except FileNotFoundError as exc:
            raise ProcessorExecutionError(
                error_code="FFPROBE_NOT_FOUND",
                user_message="FFprobe is not installed or configured",
                diagnostic_message=str(exc),
                retryable=False,
            ) from exc
        except subprocess.TimeoutExpired as exc:
            raise ProcessorExecutionError(
                error_code="FFPROBE_TIMEOUT",
                user_message="Validating the merged video timed out",
                diagnostic_message=str(exc),
                retryable=True,
            ) from exc
        if completed.returncode != 0:
            raise ProcessorExecutionError(
                error_code="FFPROBE_INVALID_OUTPUT",
                user_message="The merged video could not be validated",
                diagnostic_message=_diagnostic(completed.stderr),
                retryable=False,
            )
        try:
            payload = json.loads(completed.stdout)
            stream_types = {
                str(stream.get("codec_type"))
                for stream in payload.get("streams", [])
            }
        except (json.JSONDecodeError, AttributeError, TypeError) as exc:
            raise ProcessorExecutionError(
                error_code="FFPROBE_INVALID_OUTPUT",
                user_message="The merged video could not be validated",
                diagnostic_message=str(exc),
                retryable=False,
            ) from exc
        missing = {"video", "audio"} - stream_types
        if missing:
            stream_type = sorted(missing)[0]
            raise ProcessorExecutionError(
                error_code=f"MERGED_OUTPUT_MISSING_{stream_type.upper()}",
                user_message=f"The merged output has no {stream_type} stream",
                diagnostic_message=f"detected stream types: {sorted(stream_types)}",
                retryable=False,
            )


def _diagnostic(value: str | None) -> str:
    return (value or "no diagnostic output")[-4000:]
