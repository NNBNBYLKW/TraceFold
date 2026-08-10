from __future__ import annotations

import os
import subprocess
from collections.abc import Callable, Collection, Sequence
from pathlib import Path
from uuid import uuid4

from PIL import Image, ImageOps


class PreviewError(RuntimeError):
    pass


class UnregisteredMediaPath(PreviewError):
    pass


class PreviewGenerationFailed(PreviewError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


CommandRunner = Callable[..., subprocess.CompletedProcess[str]]


class PreviewService:
    def __init__(
        self,
        *,
        work_dir: Path,
        registered_paths: Collection[Path],
        ffmpeg_path: str = "ffmpeg",
        runner: CommandRunner = subprocess.run,
    ) -> None:
        self.previews_dir = work_dir.resolve() / "previews"
        self.registered_paths = {
            self._canonical_key(path) for path in registered_paths
        }
        self.ffmpeg_path = ffmpeg_path
        self.runner = runner

    def get_or_create(
        self,
        media_path: Path,
        media_type: str,
        sha256: str,
    ) -> Path:
        resolved = media_path.resolve()
        if self._canonical_key(resolved) not in self.registered_paths:
            raise UnregisteredMediaPath(str(media_path))
        if not resolved.is_file():
            raise PreviewGenerationFailed("MEDIA_NOT_FOUND", "registered media is missing")
        if not sha256 or not sha256.isalnum():
            raise PreviewGenerationFailed("INVALID_CACHE_KEY", "invalid preview cache key")

        self.previews_dir.mkdir(parents=True, exist_ok=True)
        target = self.previews_dir / f"{sha256.casefold()}.jpg"
        if target.is_file():
            return target

        temporary = self.previews_dir / f".{sha256}.{uuid4().hex}.tmp.jpg"
        try:
            if media_type == "image":
                self._create_image(resolved, temporary)
            elif media_type == "video":
                self._create_video(resolved, temporary)
            else:
                raise PreviewGenerationFailed(
                    "UNSUPPORTED_MEDIA_TYPE",
                    f"unsupported media type: {media_type}",
                )
            os.replace(temporary, target)
        except PreviewGenerationFailed:
            raise
        except (OSError, ValueError) as exc:
            raise PreviewGenerationFailed(
                "PREVIEW_GENERATION_FAILED",
                "could not generate preview",
            ) from exc
        finally:
            temporary.unlink(missing_ok=True)
        return target

    @staticmethod
    def _canonical_key(path: Path) -> str:
        return os.path.normcase(str(path.resolve()))

    @staticmethod
    def _create_image(source: Path, target: Path) -> None:
        with Image.open(source) as opened:
            image = ImageOps.exif_transpose(opened).convert("RGB")
            image.thumbnail((640, 360), Image.Resampling.LANCZOS)
            image.save(target, format="JPEG", quality=85, optimize=True)

    def _create_video(self, source: Path, target: Path) -> None:
        attempts = ("00:00:01", "00:00:00")
        last_message = "ffmpeg did not create a preview"
        for seek in attempts:
            target.unlink(missing_ok=True)
            command = self._video_command(source, target, seek)
            try:
                completed = self.runner(
                    command,
                    capture_output=True,
                    text=True,
                    check=False,
                )
            except OSError as exc:
                raise PreviewGenerationFailed(
                    "FFMPEG_UNAVAILABLE",
                    "ffmpeg is unavailable",
                ) from exc
            if completed.returncode == 0 and target.is_file():
                return
            last_message = completed.stderr.strip() or last_message
        raise PreviewGenerationFailed("VIDEO_PREVIEW_FAILED", last_message)

    def _video_command(
        self,
        source: Path,
        target: Path,
        seek: str,
    ) -> Sequence[str]:
        return (
            self.ffmpeg_path,
            "-y",
            "-ss",
            seek,
            "-i",
            str(source),
            "-frames:v",
            "1",
            "-vf",
            "scale=640:-2",
            str(target),
        )
