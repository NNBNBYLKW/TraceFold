from pathlib import Path

import pytest

from media_api.storage.contracts import UnsafePathError
from media_api.storage.local import LocalStorage


def test_rejects_target_outside_library(storage: LocalStorage, tmp_path: Path) -> None:
    with pytest.raises(UnsafePathError):
        storage.commit_stage(tmp_path / "stage.mp4", Path("../escape.mp4"))


@pytest.mark.parametrize(
    ("requested", "existing", "expected"),
    [
        ("clip.mp4", ["clip.mp4"], "clip (2).mp4"),
        ("clip.mp4", ["clip.mp4", "clip (2).mp4"], "clip (3).mp4"),
        ("archive.tar.mp4", ["archive.tar.mp4"], "archive.tar (2).mp4"),
    ],
)
def test_next_available_name(
    storage: LocalStorage,
    requested: str,
    existing: list[str],
    expected: str,
) -> None:
    for name in existing:
        (storage.library_dir / name).write_bytes(b"x")

    assert storage.next_available_name(requested) == expected
