from __future__ import annotations

import os
import shutil
from pathlib import Path
from uuid import uuid4

from media_api.infrastructure.config import BootstrapSettings
from media_api.storage.contracts import (
    FileChangedError,
    FileSnapshot,
    InsufficientSpaceError,
    RuntimePaths,
    TargetExistsError,
    UnsafePathError,
)
from media_api.storage.hashing import sha256 as calculate_sha256


class LocalStorage:
    def __init__(self, settings: BootstrapSettings) -> None:
        self.inbox_dir = settings.inbox_dir.resolve()
        self.library_dir = settings.library_dir.resolve()
        self.work_dir = settings.work_dir.resolve()
        self.staging_dir = self.library_dir / ".staging"
        self.jobs_dir = self.work_dir / "jobs"
        self.backups_dir = self.work_dir / "backups"
        self.previews_dir = self.work_dir / "previews"

    def snapshot(self, path: Path) -> FileSnapshot:
        resolved = self._ensure_managed(path)
        stat = resolved.stat()
        if not resolved.is_file():
            raise UnsafePathError(f"managed path is not a file: {resolved}")
        return FileSnapshot(path=resolved, size=stat.st_size, modified_ns=stat.st_mtime_ns)

    def sha256(self, path: Path) -> str:
        return calculate_sha256(self._ensure_managed_file(path))

    def provision_directories(self, paths: RuntimePaths) -> None:
        expected = RuntimePaths(
            inbox_dir=self.inbox_dir,
            library_dir=self.library_dir,
            work_dir=self.work_dir,
        )
        resolved = RuntimePaths(
            inbox_dir=paths.inbox_dir.resolve(),
            library_dir=paths.library_dir.resolve(),
            work_dir=paths.work_dir.resolve(),
        )
        if resolved != expected:
            raise UnsafePathError("runtime paths do not match validated bootstrap settings")

        for directory in (
            self.inbox_dir,
            self.library_dir,
            self.work_dir,
            self.staging_dir,
            self.jobs_dir,
            self.backups_dir,
            self.previews_dir,
        ):
            directory.mkdir(parents=True, exist_ok=True)

    def reconfigure(self, settings: BootstrapSettings) -> None:
        candidate = LocalStorage(settings)
        candidate.provision_directories(
            RuntimePaths(
                inbox_dir=candidate.inbox_dir,
                library_dir=candidate.library_dir,
                work_dir=candidate.work_dir,
            )
        )
        self.inbox_dir = candidate.inbox_dir
        self.library_dir = candidate.library_dir
        self.work_dir = candidate.work_dir
        self.staging_dir = candidate.staging_dir
        self.jobs_dir = candidate.jobs_dir
        self.backups_dir = candidate.backups_dir
        self.previews_dir = candidate.previews_dir

    def copy_to_stage(
        self,
        operation_id: str,
        source: Path,
        actual_filename: str,
    ) -> Path:
        safe_operation_id = _safe_component(operation_id, label="operation_id")
        safe_filename = _safe_component(actual_filename, label="actual_filename")
        source_path = self._ensure_managed_file(source)
        source_size = source_path.stat().st_size

        if shutil.disk_usage(self.staging_dir).free < source_size:
            raise InsufficientSpaceError("not enough free space for verified staging copy")

        operation_dir = ensure_within(self.staging_dir / safe_operation_id, self.staging_dir)
        operation_dir.mkdir(parents=True, exist_ok=True)
        staged_path = ensure_within(operation_dir / safe_filename, operation_dir)
        source_hash_before = calculate_sha256(source_path)

        if staged_path.exists():
            if calculate_sha256(staged_path) == source_hash_before:
                return staged_path
            raise FileChangedError("existing staged file does not match the source")

        temporary_path = ensure_within(
            operation_dir / f".{safe_filename}.{uuid4().hex}.copying",
            operation_dir,
        )
        try:
            shutil.copy2(source_path, temporary_path)
            _fsync_file(temporary_path)
            source_hash_after = calculate_sha256(source_path)
            staged_hash = calculate_sha256(temporary_path)
            if source_hash_before != source_hash_after or staged_hash != source_hash_after:
                raise FileChangedError("source changed while it was copied to staging")
            os.replace(temporary_path, staged_path)
        finally:
            temporary_path.unlink(missing_ok=True)

        return staged_path

    def commit_stage(self, staged: Path, relative_target: Path) -> Path:
        staged_path = ensure_within(staged, self.staging_dir)
        if not staged_path.is_file():
            raise UnsafePathError(f"staged path is not a file: {staged_path}")

        target_name = _safe_component(str(relative_target), label="relative_target")
        target = ensure_within(self.library_dir / target_name, self.library_dir)
        if target.exists():
            raise TargetExistsError(f"formal target already exists: {target.name}")

        os.replace(staged_path, target)
        return target

    def backup_existing(self, operation_id: str, target: Path) -> Path:
        safe_operation_id = _safe_component(operation_id, label="operation_id")
        target_path = self._ensure_library_file(target)
        operation_dir = ensure_within(self.backups_dir / safe_operation_id, self.backups_dir)
        operation_dir.mkdir(parents=True, exist_ok=True)
        backup = ensure_within(operation_dir / target_path.name, operation_dir)
        if backup.exists():
            raise TargetExistsError(f"backup already exists: {backup}")

        expected_hash = calculate_sha256(target_path)
        temporary_path = operation_dir / f".{target_path.name}.{uuid4().hex}.copying"
        try:
            shutil.copy2(target_path, temporary_path)
            _fsync_file(temporary_path)
            if calculate_sha256(temporary_path) != expected_hash:
                raise FileChangedError("backup copy does not match the formal file")
            os.replace(temporary_path, backup)
        finally:
            temporary_path.unlink(missing_ok=True)

        self.delete_verified(target_path, expected_hash)
        return backup

    def restore_backup(self, backup: Path, target: Path) -> None:
        backup_path = ensure_within(backup, self.backups_dir)
        if not backup_path.is_file():
            raise UnsafePathError(f"backup path is not a file: {backup_path}")

        target_path = ensure_within(target, self.library_dir)
        if target_path.parent != self.library_dir:
            raise UnsafePathError("formal files must be restored to the library root")
        if target_path.exists():
            raise TargetExistsError(f"restore target already exists: {target_path.name}")

        expected_hash = calculate_sha256(backup_path)
        recovery_id = f"restore-{uuid4().hex}"
        staged = self.copy_to_stage(recovery_id, backup_path, target_path.name)
        if calculate_sha256(staged) != expected_hash:
            raise FileChangedError("restored staging copy does not match the backup")
        self.commit_stage(staged, Path(target_path.name))
        self.remove_empty_stage(recovery_id)
        self.delete_verified(backup_path, expected_hash)
        self.remove_empty_backup(backup_path.parent.name)

    def delete_verified(self, path: Path, expected_sha256: str) -> None:
        resolved = self._ensure_managed(path)
        if not resolved.exists():
            return
        if not resolved.is_file():
            raise UnsafePathError(f"managed path is not a file: {resolved}")
        if calculate_sha256(resolved) != expected_sha256:
            raise FileChangedError(f"file changed before verified deletion: {resolved}")
        resolved.unlink()

    def remove_empty_stage(self, operation_id: str) -> None:
        self._remove_empty_operation_dir(
            operation_id,
            root=self.staging_dir,
            label="staging",
        )

    def remove_empty_backup(self, operation_id: str) -> None:
        self._remove_empty_operation_dir(
            operation_id,
            root=self.backups_dir,
            label="backup",
        )

    @staticmethod
    def _remove_empty_operation_dir(
        operation_id: str,
        *,
        root: Path,
        label: str,
    ) -> None:
        safe_operation_id = _safe_component(operation_id, label="operation_id")
        operation_dir = ensure_within(
            root / safe_operation_id,
            root,
        )
        if not operation_dir.exists():
            return
        if not operation_dir.is_dir() or any(operation_dir.iterdir()):
            raise UnsafePathError(
                f"operation {label} directory is not empty: {operation_dir}"
            )
        operation_dir.rmdir()

    def next_available_name(self, actual_filename: str) -> str:
        safe_filename = _safe_component(actual_filename, label="actual_filename")
        existing_names = {path.name.casefold() for path in self.library_dir.iterdir()}
        if safe_filename.casefold() not in existing_names:
            return safe_filename

        requested = Path(safe_filename)
        counter = 2
        while True:
            candidate = f"{requested.stem} ({counter}){requested.suffix}"
            if candidate.casefold() not in existing_names:
                return candidate
            counter += 1

    def _ensure_managed(self, path: Path) -> Path:
        for root in (self.inbox_dir, self.library_dir, self.work_dir):
            try:
                return ensure_within(path, root)
            except UnsafePathError:
                continue
        raise UnsafePathError(f"path is outside managed roots: {path}")

    def _ensure_managed_file(self, path: Path) -> Path:
        resolved = self._ensure_managed(path)
        if not resolved.is_file():
            raise UnsafePathError(f"managed path is not a file: {resolved}")
        return resolved

    def _ensure_library_file(self, path: Path) -> Path:
        resolved = ensure_within(path, self.library_dir)
        if resolved.parent != self.library_dir or not resolved.is_file():
            raise UnsafePathError(f"formal path is not a library-root file: {resolved}")
        return resolved


def ensure_within(path: Path, root: Path) -> Path:
    resolved_root = root.resolve()
    resolved = path.resolve()
    try:
        resolved.relative_to(resolved_root)
    except ValueError as exc:
        raise UnsafePathError(f"path escapes managed root: {path}") from exc
    return resolved


def _safe_component(value: str, *, label: str) -> str:
    if not value or value in {".", ".."} or Path(value).name != value:
        raise UnsafePathError(f"{label} must be one safe path component")
    return value


def _fsync_file(path: Path) -> None:
    with path.open("rb+") as copied_file:
        copied_file.flush()
        os.fsync(copied_file.fileno())
