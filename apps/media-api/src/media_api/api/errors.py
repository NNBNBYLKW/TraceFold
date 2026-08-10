from __future__ import annotations

import logging
from dataclasses import dataclass
from uuid import uuid4

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from media_api.catalog.conflicts import (
    ConflictAlreadyResolved,
    ConflictChoiceExpired,
    ConflictIdempotencyKeyReused,
    ConflictNotFound,
    ConflictReplacementUnavailable,
)
from media_api.catalog.ingest import (
    IdempotencyKeyReused,
    InboxNotReady,
    IngestError,
    SourceChanged,
)
from media_api.catalog.service import (
    CatalogTaxonomyNotFound,
    MediaAssetNotFound,
    RegisteredAssetFileMissing,
    RevealFailed,
    UnsafeRegisteredAssetPath,
)
from media_api.intake.service import (
    InboxActionNotAllowed,
    InboxItemNotFound,
    TaxonomySelectionNotFound,
)
from media_api.processing.planner import (
    NoApplicableProcessor,
    ProcessingCommandError,
    ProcessingIdempotencyKeyReused,
    ProcessingItemNotFound,
    ProcessingPlanError,
)
from media_api.settings.service import SettingsBusy, SettingsProcessorNotFound
from media_api.taxonomy.service import (
    EmptyTaxonomyName,
    TaxonomyNameConflict,
    TaxonomyNotFound,
)


logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class ErrorDefinition:
    status_code: int
    code: str
    message: str


DOMAIN_ERROR_REGISTRY: dict[type[Exception], ErrorDefinition] = {
    SourceChanged: ErrorDefinition(
        status_code=409,
        code="SOURCE_CHANGED",
        message="源文件已发生变化，请重新扫描。",
    ),
    InboxNotReady: ErrorDefinition(
        status_code=409,
        code="INBOX_NOT_READY",
        message="当前项目尚不能正式入库。",
    ),
    IdempotencyKeyReused: ErrorDefinition(409, "IDEMPOTENCY_KEY_REUSED", "请求标识已被其他操作使用。"),
    ConflictNotFound: ErrorDefinition(404, "CONFLICT_NOT_FOUND", "未找到冲突记录。"),
    ConflictChoiceExpired: ErrorDefinition(409, "CONFLICT_CHOICE_EXPIRED", "源文件已变化，请重新选择。"),
    ConflictAlreadyResolved: ErrorDefinition(409, "CONFLICT_ALREADY_RESOLVED", "该冲突已经处理。"),
    ConflictIdempotencyKeyReused: ErrorDefinition(409, "IDEMPOTENCY_KEY_REUSED", "请求标识已被其他冲突决策使用。"),
    ConflictReplacementUnavailable: ErrorDefinition(409, "CONFLICT_REPLACEMENT_UNAVAILABLE", "当前冲突目标不能安全替换。"),
    MediaAssetNotFound: ErrorDefinition(404, "MEDIA_ASSET_NOT_FOUND", "未找到媒体资产。"),
    CatalogTaxonomyNotFound: ErrorDefinition(404, "TAXONOMY_NOT_FOUND", "未找到分类条目。"),
    RegisteredAssetFileMissing: ErrorDefinition(409, "MEDIA_FILE_MISSING", "登记的媒体文件不存在。"),
    UnsafeRegisteredAssetPath: ErrorDefinition(409, "MEDIA_PATH_INVALID", "登记的媒体路径无效。"),
    RevealFailed: ErrorDefinition(503, "REVEAL_FAILED", "无法打开系统文件管理器。"),
    InboxItemNotFound: ErrorDefinition(404, "INBOX_ITEM_NOT_FOUND", "未找到待入库项目。"),
    TaxonomySelectionNotFound: ErrorDefinition(404, "TAXONOMY_NOT_FOUND", "未找到分类条目。"),
    InboxActionNotAllowed: ErrorDefinition(409, "INBOX_ACTION_NOT_ALLOWED", "当前状态不能执行此操作。"),
    ProcessingItemNotFound: ErrorDefinition(404, "INBOX_ITEM_NOT_FOUND", "未找到待入库项目。"),
    ProcessingIdempotencyKeyReused: ErrorDefinition(409, "IDEMPOTENCY_KEY_REUSED", "请求标识已被其他处理任务使用。"),
    NoApplicableProcessor: ErrorDefinition(409, "NO_APPLICABLE_PROCESSOR", "当前文件不需要可用的前置处理。"),
    ProcessingPlanError: ErrorDefinition(409, "PROCESSING_PLAN_INVALID", "无法创建前置处理计划。"),
    ProcessingCommandError: ErrorDefinition(409, "PROCESSING_COMMAND_REJECTED", "当前状态不能启动前置处理。"),
    SettingsBusy: ErrorDefinition(409, "SETTINGS_BUSY", "存在进行中的任务，暂时不能修改运行目录。"),
    SettingsProcessorNotFound: ErrorDefinition(404, "PROCESSOR_NOT_FOUND", "未找到处理器。"),
    EmptyTaxonomyName: ErrorDefinition(422, "TAXONOMY_NAME_EMPTY", "分类名称不能为空。"),
    TaxonomyNotFound: ErrorDefinition(404, "TAXONOMY_NOT_FOUND", "未找到分类条目。"),
    TaxonomyNameConflict: ErrorDefinition(409, "TAXONOMY_NAME_CONFLICT", "分类名称已存在。"),
    IngestError: ErrorDefinition(409, "INGEST_FAILED", "正式入库未能完成。"),
}

SYSTEM_RECOVERING = ErrorDefinition(
    status_code=503,
    code="SYSTEM_RECOVERING",
    message="系统正在恢复未完成操作，请稍后重试。",
)


class ApiError(Exception):
    def __init__(
        self,
        definition: ErrorDefinition,
        *,
        details: dict[str, object] | None = None,
        diagnostic: str | None = None,
    ) -> None:
        super().__init__(definition.code)
        self.definition = definition
        self.details = details or {}
        self.diagnostic = diagnostic


def domain_api_error(
    error: Exception,
    *,
    details: dict[str, object] | None = None,
) -> ApiError:
    for error_type, definition in DOMAIN_ERROR_REGISTRY.items():
        if isinstance(error, error_type):
            return ApiError(
                definition,
                details=details,
                diagnostic=str(error),
            )
    raise LookupError(f"unregistered domain error: {type(error).__name__}")


def install_error_contract(app: FastAPI) -> None:
    @app.middleware("http")
    async def correlation_id_middleware(request: Request, call_next):
        correlation_id = request.headers.get("X-Correlation-ID") or str(uuid4())
        request.state.correlation_id = correlation_id
        response = await call_next(request)
        response.headers["X-Correlation-ID"] = correlation_id
        return response

    @app.exception_handler(ApiError)
    async def handle_api_error(request: Request, error: ApiError) -> JSONResponse:
        correlation_id = getattr(request.state, "correlation_id", "unknown")
        logger.warning(
            "domain request failed correlation_id=%s code=%s diagnostic=%s",
            correlation_id,
            error.definition.code,
            error.diagnostic,
        )
        return JSONResponse(
            status_code=error.definition.status_code,
            content={
                "error": {
                    "code": error.definition.code,
                    "message": error.definition.message,
                    "details": error.details,
                }
            },
        )

    @app.exception_handler(HTTPException)
    async def handle_http_error(request: Request, error: HTTPException) -> JSONResponse:
        raw_detail = error.detail
        details: dict[str, object] = {}
        if isinstance(raw_detail, dict):
            code = str(raw_detail.get("code", f"HTTP_{error.status_code}"))
            details = {
                str(key): value
                for key, value in raw_detail.items()
                if key != "code"
            }
            message = "请求无法完成。"
        else:
            code = {
                404: "NOT_FOUND",
                409: "CONFLICT",
                422: "VALIDATION_ERROR",
                503: "SERVICE_UNAVAILABLE",
            }.get(error.status_code, f"HTTP_{error.status_code}")
            message = str(raw_detail) if raw_detail else "请求无法完成。"
        _log_http_failure(request, code, str(raw_detail))
        return JSONResponse(
            status_code=error.status_code,
            content={
                "error": {
                    "code": code,
                    "message": message,
                    "details": details,
                }
            },
            headers=error.headers,
        )

    @app.exception_handler(RequestValidationError)
    async def handle_validation_error(
        request: Request,
        error: RequestValidationError,
    ) -> JSONResponse:
        fields = [
            {
                "location": [str(value) for value in entry["loc"]],
                "type": entry["type"],
            }
            for entry in error.errors()
        ]
        _log_http_failure(request, "VALIDATION_ERROR", repr(fields))
        return JSONResponse(
            status_code=422,
            content={
                "error": {
                    "code": "VALIDATION_ERROR",
                    "message": "请求参数无效。",
                    "details": {"fields": fields},
                }
            },
        )


def _log_http_failure(request: Request, code: str, diagnostic: str) -> None:
    logger.warning(
        "request failed correlation_id=%s code=%s diagnostic=%s",
        getattr(request.state, "correlation_id", "unknown"),
        code,
        diagnostic,
    )
