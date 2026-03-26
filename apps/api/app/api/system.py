from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.api.schemas import (
    CaptureBundleExportRead,
    CaptureBundleExportRequest,
    CaptureBundleImportRead,
    CaptureBundleImportRequest,
    HealthzRead,
    LocalBackupRead,
    LocalBackupRequest,
    LocalOperabilityRead,
    LocalRestoreRead,
    LocalRestoreRequest,
    PingRead,
    RuntimeStatusRead,
)
from app.core.local_operability import (
    create_local_backup,
    export_capture_bundle,
    get_local_operability_read,
    import_capture_bundle,
    restore_local_backup,
)
from app.core.responses import ApiResponse, success_response
from app.core.runtime_status import get_runtime_status_read
from app.db.session import get_db


router = APIRouter()


@router.get("/ping", tags=["system"], response_model=ApiResponse[PingRead])
def ping() -> ApiResponse[PingRead]:
    return success_response(data=PingRead(message="pong"), message="Ping OK.")


@router.get("/healthz", tags=["system"], response_model=ApiResponse[HealthzRead])
def healthz() -> ApiResponse[HealthzRead]:
    return success_response(data=HealthzRead(status="ok"), message="Health check OK.")


@router.get("/system/status", tags=["system"], response_model=ApiResponse[RuntimeStatusRead])
def get_runtime_status() -> ApiResponse[RuntimeStatusRead]:
    return success_response(
        data=get_runtime_status_read(),
        message="Runtime status fetched.",
    )


@router.get(
    "/system/local-operability",
    tags=["system"],
    response_model=ApiResponse[LocalOperabilityRead],
)
def get_local_operability(
    db: Session = Depends(get_db),
) -> ApiResponse[LocalOperabilityRead]:
    return success_response(
        data=get_local_operability_read(db),
        message="Local operability status fetched.",
    )


@router.post(
    "/system/backup",
    tags=["system"],
    response_model=ApiResponse[LocalBackupRead],
)
def backup_local_database(
    payload: LocalBackupRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[LocalBackupRead]:
    return success_response(
        data=create_local_backup(db, destination_path=payload.destination_path),
        message="Local backup created.",
    )


@router.post(
    "/system/restore",
    tags=["system"],
    response_model=ApiResponse[LocalRestoreRead],
)
def restore_local_database(
    payload: LocalRestoreRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[LocalRestoreRead]:
    return success_response(
        data=restore_local_backup(
            db,
            source_path=payload.source_path,
            create_safety_backup=payload.create_safety_backup,
        ),
        message="Local database restored.",
    )


@router.post(
    "/system/export/capture-bundle",
    tags=["system"],
    response_model=ApiResponse[CaptureBundleExportRead],
)
def export_capture_bundle_route(
    payload: CaptureBundleExportRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[CaptureBundleExportRead]:
    return success_response(
        data=export_capture_bundle(db, destination_path=payload.destination_path),
        message="Capture bundle exported.",
    )


@router.post(
    "/system/import/capture-bundle",
    tags=["system"],
    response_model=ApiResponse[CaptureBundleImportRead],
)
def import_capture_bundle_route(
    payload: CaptureBundleImportRequest,
    db: Session = Depends(get_db),
) -> ApiResponse[CaptureBundleImportRead]:
    return success_response(
        data=import_capture_bundle(db, source_path=payload.source_path),
        message="Capture bundle imported.",
    )
