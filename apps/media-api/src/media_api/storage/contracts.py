from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Protocol


@dataclass(frozen=True)
class FileSnapshot:
    path: Path
    size: int
    modified_ns: int


@dataclass(frozen=True)
class RuntimePaths:
    inbox_dir: Path
    library_dir: Path
    work_dir: Path


class StorageError(RuntimeError):
    """Base class for safe-storage failures."""


class UnsafePathError(StorageError):
    pass


class FileChangedError(StorageError):
    pass


class InsufficientSpaceError(StorageError):
    pass


class TargetExistsError(StorageError):
    pass


class StoragePort(Protocol):
    def snapshot(self, path: Path) -> FileSnapshot: ...

    def sha256(self, path: Path) -> str: ...

    def provision_directories(self, paths: RuntimePaths) -> None: ...

    def copy_to_stage(
        self,
        operation_id: str,
        source: Path,
        actual_filename: str,
    ) -> Path: ...

    def commit_stage(self, staged: Path, relative_target: Path) -> Path: ...

    def backup_existing(self, operation_id: str, target: Path) -> Path: ...

    def restore_backup(self, backup: Path, target: Path) -> None: ...

    def delete_verified(self, path: Path, expected_sha256: str) -> None: ...

    def remove_empty_stage(self, operation_id: str) -> None: ...

    def next_available_name(self, actual_filename: str) -> str: ...
