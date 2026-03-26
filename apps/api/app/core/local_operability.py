from __future__ import annotations

from datetime import UTC, datetime
import json
from pathlib import Path
import shutil
from typing import Any

from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session

from app.api.schemas import (
    CaptureBundleExportRead,
    CaptureBundleImportRead,
    LocalBackupRead,
    LocalOperabilityRead,
    LocalRestoreRead,
)
from app.core.exceptions import BadRequestError, NotFoundError
from app.db.database_url import resolve_database_url
from app.domains.capture import repository as capture_repository
from app.services.intake import service as intake_service


_SQLITE_URL_PREFIX = "sqlite:///"
_CAPTURE_BUNDLE_TYPE = "tracefold.capture_bundle.v1"


def get_local_operability_read(db: Session) -> LocalOperabilityRead:
    database_path = _resolve_sqlite_database_path(db)
    backup_directory = _default_backup_directory(database_path)
    transfer_directory = _default_transfer_directory(database_path)
    daily_use_readiness, readiness_message, warnings = _evaluate_daily_use_readiness(database_path)

    return LocalOperabilityRead(
        database_path=str(database_path),
        database_exists=database_path.exists(),
        backup_directory=str(backup_directory),
        transfer_directory=str(transfer_directory),
        daily_use_readiness=daily_use_readiness,
        readiness_message=readiness_message,
        warnings=warnings,
        guidance=[
            "SQLite remains the single source of truth for local TraceFold data.",
            "Use backup and restore for full local recovery and continuity.",
            "Capture bundle import and export stay intentionally narrower than full database backup.",
        ],
        backup_scope="Backup copies the active SQLite database file to an explicit local path.",
        restore_scope="Restore replaces the active SQLite database file and creates a safety backup first by default.",
        export_scope="Capture bundle export writes plain captured text inputs that can be re-submitted through the existing intake path.",
        import_scope="Capture bundle import creates new capture records and re-runs the existing backend intake semantics.",
    )


def create_local_backup(
    db: Session,
    *,
    destination_path: str | None = None,
) -> LocalBackupRead:
    database_path = _resolve_sqlite_database_path(db)
    if not database_path.exists():
        raise NotFoundError(
            message="SQLite database file was not found for local backup.",
            code="DATABASE_FILE_NOT_FOUND",
            details={"database_path": str(database_path)},
        )

    backup_directory = _default_backup_directory(database_path)
    backup_directory.mkdir(parents=True, exist_ok=True)
    target_path = _resolve_output_path(
        destination_path,
        default_path=backup_directory / f"{database_path.stem}-backup-{_timestamp_slug()}{database_path.suffix or '.db'}",
    )
    _ensure_not_same_path(source_path=database_path, target_path=target_path, operation="backup")

    target_path.parent.mkdir(parents=True, exist_ok=True)
    _release_database_handles(db)
    shutil.copy2(database_path, target_path)

    return LocalBackupRead(
        backup_created=True,
        database_path=str(database_path),
        backup_path=str(target_path),
        file_size_bytes=target_path.stat().st_size,
        created_at=_utcnow(),
    )


def restore_local_backup(
    db: Session,
    *,
    source_path: str,
    create_safety_backup: bool = True,
) -> LocalRestoreRead:
    database_path = _resolve_sqlite_database_path(db)
    restore_source_path = _resolve_existing_path(source_path, operation="restore")
    _ensure_not_same_path(source_path=restore_source_path, target_path=database_path, operation="restore")

    backup_directory = _default_backup_directory(database_path)
    backup_directory.mkdir(parents=True, exist_ok=True)
    safety_backup_path: Path | None = None

    _release_database_handles(db)

    if create_safety_backup and database_path.exists():
        safety_backup_path = backup_directory / f"{database_path.stem}-pre-restore-{_timestamp_slug()}{database_path.suffix or '.db'}"
        shutil.copy2(database_path, safety_backup_path)

    database_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(restore_source_path, database_path)

    return LocalRestoreRead(
        restore_completed=True,
        source_path=str(restore_source_path),
        database_path=str(database_path),
        safety_backup_path=None if safety_backup_path is None else str(safety_backup_path),
        restored_at=_utcnow(),
    )


def export_capture_bundle(
    db: Session,
    *,
    destination_path: str | None = None,
) -> CaptureBundleExportRead:
    database_path = _resolve_sqlite_database_path(db)
    transfer_directory = _default_transfer_directory(database_path)
    transfer_directory.mkdir(parents=True, exist_ok=True)
    export_path = _resolve_output_path(
        destination_path,
        default_path=transfer_directory / f"{database_path.stem}-capture-bundle-{_timestamp_slug()}.json",
    )
    export_path.parent.mkdir(parents=True, exist_ok=True)

    items: list[dict[str, Any]] = []
    skipped_count = 0
    for capture in capture_repository.list_captures_for_export(db):
        raw_text = _normalize_optional_text(capture.raw_text)
        if raw_text is None:
            skipped_count += 1
            continue

        items.append(
            {
                "source_type": capture.source_type,
                "source_ref": capture.source_ref,
                "raw_text": raw_text,
                "captured_at": capture.created_at.isoformat(),
            }
        )

    bundle = {
        "bundle_type": _CAPTURE_BUNDLE_TYPE,
        "created_at": _utcnow().isoformat(),
        "source_database_path": str(database_path),
        "items": items,
    }
    export_path.write_text(
        json.dumps(bundle, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    return CaptureBundleExportRead(
        export_created=True,
        export_path=str(export_path),
        item_count=len(items),
        skipped_count=skipped_count,
        created_at=_utcnow(),
    )


def import_capture_bundle(
    db: Session,
    *,
    source_path: str,
) -> CaptureBundleImportRead:
    import_path = _resolve_existing_path(source_path, operation="import")
    try:
        payload = json.loads(import_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise BadRequestError(
            message="Capture bundle file is not valid JSON.",
            code="INVALID_CAPTURE_BUNDLE",
            details={"source_path": str(import_path)},
        ) from exc

    items = _validate_capture_bundle(payload, source_path=str(import_path))
    imported_count = 0
    pending_count = 0
    committed_count = 0

    try:
        for item in items:
            capture = intake_service.submit_capture(
                db,
                source_type=item["source_type"],
                source_ref=item["source_ref"],
                raw_text=item["raw_text"],
            )
            outcome = intake_service.process_capture(db, capture=capture)
            imported_count += 1
            if str(outcome["route"]) == "pending":
                pending_count += 1
            else:
                committed_count += 1
        db.commit()
    except Exception:
        db.rollback()
        raise

    return CaptureBundleImportRead(
        import_completed=True,
        source_path=str(import_path),
        imported_count=imported_count,
        pending_count=pending_count,
        committed_count=committed_count,
        imported_at=_utcnow(),
    )


def _validate_capture_bundle(payload: Any, *, source_path: str) -> list[dict[str, str | None]]:
    if not isinstance(payload, dict):
        raise BadRequestError(
            message="Capture bundle file must contain a JSON object.",
            code="INVALID_CAPTURE_BUNDLE",
            details={"source_path": source_path},
        )

    if payload.get("bundle_type") != _CAPTURE_BUNDLE_TYPE:
        raise BadRequestError(
            message="Capture bundle file type is not supported.",
            code="INVALID_CAPTURE_BUNDLE_TYPE",
            details={"source_path": source_path, "bundle_type": payload.get("bundle_type")},
        )

    raw_items = payload.get("items")
    if not isinstance(raw_items, list):
        raise BadRequestError(
            message="Capture bundle file must contain an items list.",
            code="INVALID_CAPTURE_BUNDLE",
            details={"source_path": source_path},
        )

    items: list[dict[str, str | None]] = []
    for index, raw_item in enumerate(raw_items):
        if not isinstance(raw_item, dict):
            raise BadRequestError(
                message="Capture bundle items must be objects.",
                code="INVALID_CAPTURE_BUNDLE_ITEM",
                details={"source_path": source_path, "item_index": index},
            )
        source_type = _normalize_optional_text(raw_item.get("source_type"))
        raw_text = _normalize_optional_text(raw_item.get("raw_text"))
        source_ref = _normalize_optional_text(raw_item.get("source_ref"))
        if source_type is None or raw_text is None:
            raise BadRequestError(
                message="Capture bundle items must include source_type and raw_text.",
                code="INVALID_CAPTURE_BUNDLE_ITEM",
                details={"source_path": source_path, "item_index": index},
            )
        items.append(
            {
                "source_type": source_type,
                "source_ref": source_ref,
                "raw_text": raw_text,
            }
        )

    return items


def _resolve_sqlite_database_path(db: Session) -> Path:
    bind = db.get_bind()
    database_url = str(bind.url) if hasattr(bind, "url") else ""
    resolved_database_url = resolve_database_url(database_url)
    if not resolved_database_url.startswith(_SQLITE_URL_PREFIX):
        raise BadRequestError(
            message="Local operability actions require a local SQLite database.",
            code="LOCAL_SQLITE_REQUIRED",
        )
    return Path(resolved_database_url[len(_SQLITE_URL_PREFIX) :]).resolve()


def _default_backup_directory(database_path: Path) -> Path:
    return database_path.parent / "backups"


def _default_transfer_directory(database_path: Path) -> Path:
    return database_path.parent / "transfers"


def _evaluate_daily_use_readiness(database_path: Path) -> tuple[str, str, list[str]]:
    normalized_name = database_path.name.lower()
    if any(token in normalized_name for token in ("demo", "acceptance", "seed")):
        return (
            "demo_path",
            "This SQLite path still looks like demo or acceptance data. Move daily-use work to a dedicated local database path before relying on long-running backups.",
            [
                "Current database filename still looks demo-oriented.",
                "Daily-use backup is safer after TRACEFOLD_API_DB_URL points to a dedicated local database file.",
            ],
        )

    if not database_path.exists():
        return (
            "database_missing",
            "The configured SQLite path does not exist yet. Initialize the local database before relying on backup or transfer flows.",
            [
                "SQLite database file is missing at the configured path.",
            ],
        )

    return (
        "daily_use_ready",
        "The active SQLite path looks suitable for regular local use. Use backup for full safety and capture bundle transfer only when you need bounded upstream data movement.",
        [],
    )


def _resolve_output_path(destination_path: str | None, *, default_path: Path) -> Path:
    if destination_path is None or not destination_path.strip():
        return default_path.resolve()

    candidate = Path(destination_path.strip())
    if not candidate.is_absolute():
        candidate = (Path.cwd() / candidate).resolve()
    else:
        candidate = candidate.resolve()
    return candidate


def _resolve_existing_path(source_path: str, *, operation: str) -> Path:
    candidate = _resolve_output_path(source_path, default_path=Path(source_path))
    if not candidate.exists():
        raise NotFoundError(
            message=f"Local {operation} path was not found.",
            code="LOCAL_PATH_NOT_FOUND",
            details={"source_path": str(candidate)},
        )
    if not candidate.is_file():
        raise BadRequestError(
            message=f"Local {operation} path must point to a file.",
            code="LOCAL_PATH_NOT_FILE",
            details={"source_path": str(candidate)},
        )
    return candidate


def _ensure_not_same_path(*, source_path: Path, target_path: Path, operation: str) -> None:
    if source_path.resolve() == target_path.resolve():
        raise BadRequestError(
            message=f"Local {operation} path must be different from the active SQLite database path.",
            code="LOCAL_PATH_CONFLICT",
            details={"path": str(source_path)},
        )


def _release_database_handles(db: Session) -> None:
    bind = db.get_bind()
    db.close()
    if isinstance(bind, Engine):
        bind.dispose()


def _normalize_optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = " ".join(str(value).split())
    return normalized or None


def _timestamp_slug() -> str:
    return _utcnow().strftime("%Y%m%d-%H%M%S")


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
