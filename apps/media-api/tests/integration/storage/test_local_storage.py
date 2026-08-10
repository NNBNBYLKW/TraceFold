from pathlib import Path

import pytest

from media_api.storage.contracts import FileChangedError
from media_api.storage.local import LocalStorage


def test_staging_preserves_source(storage: LocalStorage, inbox_file: Path) -> None:
    staged = storage.copy_to_stage("op-1", inbox_file, "clip.mp4")

    assert inbox_file.exists()
    assert staged.exists()
    assert staged.parent.name == "op-1"
    assert staged.read_bytes() == inbox_file.read_bytes()


def test_delete_requires_expected_hash(storage: LocalStorage, inbox_file: Path) -> None:
    with pytest.raises(FileChangedError):
        storage.delete_verified(inbox_file, expected_sha256="wrong")

    assert inbox_file.exists()


def test_commit_moves_verified_stage_without_deleting_source(
    storage: LocalStorage,
    inbox_file: Path,
) -> None:
    staged = storage.copy_to_stage("op-commit", inbox_file, "clip.mp4")

    final = storage.commit_stage(staged, Path("clip.mp4"))

    assert final == storage.library_dir / "clip.mp4"
    assert final.read_bytes() == b"media-bytes"
    assert inbox_file.exists()
    assert not staged.exists()


def test_backup_and_restore_round_trip_preserves_formal_bytes(
    storage: LocalStorage,
) -> None:
    target = storage.library_dir / "existing.mp4"
    target.write_bytes(b"existing-media")

    backup = storage.backup_existing("op-replace", target)

    assert backup.read_bytes() == b"existing-media"
    assert not target.exists()

    storage.restore_backup(backup, target)

    assert target.read_bytes() == b"existing-media"
    assert not backup.exists()
    assert list(storage.staging_dir.iterdir()) == []
    assert list(storage.backups_dir.iterdir()) == []
