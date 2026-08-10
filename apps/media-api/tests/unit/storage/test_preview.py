from __future__ import annotations

from pathlib import Path

import pytest
from PIL import Image

from media_api.storage.preview import PreviewService, UnregisteredMediaPath


@pytest.fixture
def image_path(tmp_path: Path) -> Path:
    path = tmp_path / "registered" / "photo.jpg"
    path.parent.mkdir()
    Image.new("RGB", (1200, 800), "#315375").save(path, quality=80)
    return path


@pytest.fixture
def preview_service(tmp_path: Path, image_path: Path) -> PreviewService:
    return PreviewService(
        work_dir=tmp_path / "work",
        registered_paths={image_path},
    )


def test_image_preview_is_cached_by_content_hash(
    preview_service: PreviewService,
    image_path: Path,
) -> None:
    first = preview_service.get_or_create(image_path, "image", "abc123")
    first_modified = first.stat().st_mtime_ns
    second = preview_service.get_or_create(image_path, "image", "abc123")

    assert first == second
    assert second.stat().st_mtime_ns == first_modified
    assert first.parent.name == "previews"
    assert first.suffix == ".jpg"
    with Image.open(first) as preview:
        assert preview.size == (540, 360)


def test_preview_rejects_path_outside_registered_input(
    preview_service: PreviewService,
    tmp_path: Path,
) -> None:
    unknown = tmp_path / "unknown.jpg"
    Image.new("RGB", (10, 10)).save(unknown)

    with pytest.raises(UnregisteredMediaPath):
        preview_service.get_or_create(unknown, "image", "x")
