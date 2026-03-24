from __future__ import annotations

import logging
from typing import Any

from alembic.util.exc import CommandError
from fastapi import FastAPI, HTTPException, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from sqlalchemy.exc import OperationalError

from app.core.error_codes import ErrorCode
from app.core.logging import build_log_message, get_logger
from app.core.responses import error_response


logger = get_logger(__name__)


def _error_response_content(
    *,
    message: str,
    code: str,
    details: dict[str, Any] | list[Any] | str | None = None,
    request_id: str | None = None,
    retryable: bool | None = None,
) -> dict[str, Any]:
    payload = error_response(
        message=message,
        code=code,
        details=details,
        request_id=request_id,
        retryable=retryable,
    ).model_dump()

    error_payload = payload.get("error")
    if isinstance(error_payload, dict):
        if error_payload.get("request_id") is None:
            error_payload.pop("request_id", None)
        if error_payload.get("retryable") is None:
            error_payload.pop("retryable", None)

    return payload


class AppException(Exception):
    """
    Base application exception.

    Use this for domain/service-level controlled errors that should be
    returned to the client in the standard API error envelope.
    """

    def __init__(
        self,
        *,
        message: str,
        code: str = ErrorCode.BAD_REQUEST,
        status_code: int = status.HTTP_400_BAD_REQUEST,
        details: dict[str, Any] | list[Any] | str | None = None,
        request_id: str | None = None,
        retryable: bool | None = None,
        log_level: int | None = None,
    ) -> None:
        self.message = message
        self.code = code
        self.status_code = status_code
        self.details = details
        self.request_id = request_id
        self.retryable = retryable
        self.log_level = log_level
        super().__init__(message)


class NotFoundError(AppException):
    def __init__(
        self,
        *,
        message: str = "Resource not found.",
        code: str = ErrorCode.NOT_FOUND,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_404_NOT_FOUND,
            details=details,
        )


class ConflictError(AppException):
    def __init__(
        self,
        *,
        message: str = "Resource conflict.",
        code: str = ErrorCode.CONFLICT,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_409_CONFLICT,
            details=details,
        )


class IllegalStateError(ConflictError):
    def __init__(
        self,
        *,
        message: str = "Illegal service state.",
        code: str = ErrorCode.ILLEGAL_STATE,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            details=details,
        )
        self.log_level = logging.WARNING


class BadRequestError(AppException):
    def __init__(
        self,
        *,
        message: str = "Bad request.",
        code: str = ErrorCode.BAD_REQUEST,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_400_BAD_REQUEST,
            details=details,
        )


class UnauthorizedError(AppException):
    def __init__(
        self,
        *,
        message: str = "Unauthorized.",
        code: str = ErrorCode.UNAUTHORIZED,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_401_UNAUTHORIZED,
            details=details,
        )


class ForbiddenError(AppException):
    def __init__(
        self,
        *,
        message: str = "Forbidden.",
        code: str = ErrorCode.FORBIDDEN,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_403_FORBIDDEN,
            details=details,
        )


class DependencyUnavailableError(AppException):
    def __init__(
        self,
        *,
        message: str = "Required dependency is unavailable.",
        code: str = ErrorCode.DEPENDENCY_UNAVAILABLE,
        details: dict[str, Any] | list[Any] | str | None = None,
        retryable: bool | None = True,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            details=details,
            retryable=retryable,
            log_level=logging.ERROR,
        )


class DatabaseUnavailableError(DependencyUnavailableError):
    def __init__(
        self,
        *,
        message: str = "Database is unavailable.",
        code: str = ErrorCode.DATABASE_UNAVAILABLE,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            details=details,
            retryable=True,
        )


class MigrationStateError(AppException):
    def __init__(
        self,
        *,
        message: str = "Migration state is invalid.",
        code: str = ErrorCode.MIGRATION_STATE_ERROR,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            details=details,
            retryable=False,
            log_level=logging.ERROR,
        )


class DerivationFailedError(AppException):
    def __init__(
        self,
        *,
        message: str = "Derivation failed.",
        code: str = ErrorCode.DERIVATION_FAILED,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            details=details,
            retryable=False,
            log_level=logging.ERROR,
        )


class RuleEvaluationFailedError(AppException):
    def __init__(
        self,
        *,
        message: str = "Rule evaluation failed.",
        code: str = ErrorCode.RULE_EVALUATION_FAILED,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            details=details,
            retryable=False,
            log_level=logging.ERROR,
        )


class TaskRuntimeUnavailableError(AppException):
    def __init__(
        self,
        *,
        message: str = "Task runtime is unavailable.",
        code: str = ErrorCode.TASK_RUNTIME_UNAVAILABLE,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            details=details,
            retryable=False,
            log_level=logging.ERROR,
        )


def register_exception_handlers(app: FastAPI) -> None:
    """
    Register global exception handlers.

    This should be called once during app startup / app creation.
    """

    @app.exception_handler(AppException)
    async def handle_app_exception(request: Request, exc: AppException) -> JSONResponse:
        if exc.log_level is not None:
            logger.log(
                exc.log_level,
                build_log_message(
                    "api_app_exception",
                    code=exc.code,
                    status_code=exc.status_code,
                    method=request.method,
                    path=request.url.path,
                ),
            )
        return JSONResponse(
            status_code=exc.status_code,
            content=_error_response_content(
                message=exc.message,
                code=exc.code,
                details=exc.details,
                request_id=exc.request_id,
                retryable=exc.retryable,
            ),
        )

    @app.exception_handler(RequestValidationError)
    async def handle_validation_exception(
        request: Request,
        exc: RequestValidationError,
    ) -> JSONResponse:
        logger.warning(
            build_log_message(
                "api_request_validation_failed",
                code=ErrorCode.VALIDATION_ERROR,
                method=request.method,
                path=request.url.path,
                error_count=len(exc.errors()),
            )
        )
        return JSONResponse(
            status_code=status.HTTP_400_BAD_REQUEST,
            content=_error_response_content(
                message="Validation failed.",
                code=ErrorCode.VALIDATION_ERROR,
                details=exc.errors(),
            ),
        )

    @app.exception_handler(HTTPException)
    async def handle_http_exception(request: Request, exc: HTTPException) -> JSONResponse:
        detail: Any = exc.detail

        if isinstance(detail, dict) and {"message", "code"} <= detail.keys():
            message = str(detail["message"])
            code = str(detail["code"])
            details = detail.get("details")
            request_id = detail.get("request_id")
            retryable = detail.get("retryable")
        else:
            message = str(detail) if detail is not None else "HTTP error."
            code = ErrorCode.HTTP_ERROR
            details = None
            request_id = None
            retryable = None

        logger.warning(
            build_log_message(
                "api_http_exception",
                code=code,
                status_code=exc.status_code,
                method=request.method,
                path=request.url.path,
            )
        )

        return JSONResponse(
            status_code=exc.status_code,
            content=_error_response_content(
                message=message,
                code=code,
                details=details,
                request_id=request_id,
                retryable=retryable,
            ),
        )

    @app.exception_handler(OperationalError)
    async def handle_operational_error(
        request: Request,
        exc: OperationalError,
    ) -> JSONResponse:
        logger.exception(
            build_log_message(
                "api_database_unavailable",
                code=ErrorCode.DATABASE_UNAVAILABLE,
                method=request.method,
                path=request.url.path,
            )
        )
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content=_error_response_content(
                message="Database is unavailable.",
                code=ErrorCode.DATABASE_UNAVAILABLE,
                retryable=True,
            ),
        )

    @app.exception_handler(CommandError)
    async def handle_migration_command_error(
        request: Request,
        exc: CommandError,
    ) -> JSONResponse:
        logger.exception(
            build_log_message(
                "api_migration_state_error",
                code=ErrorCode.MIGRATION_STATE_ERROR,
                method=request.method,
                path=request.url.path,
            )
        )
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content=_error_response_content(
                message="Migration state is invalid.",
                code=ErrorCode.MIGRATION_STATE_ERROR,
                details={"error": str(exc)},
                retryable=False,
            ),
        )

    @app.exception_handler(Exception)
    async def handle_unexpected_exception(
        request: Request,
        exc: Exception,
    ) -> JSONResponse:
        logger.exception(
            build_log_message(
                "api_unexpected_exception",
                code=ErrorCode.INTERNAL_SERVER_ERROR,
                method=request.method,
                path=request.url.path,
            )
        )
        return JSONResponse(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            content=_error_response_content(
                message="Internal server error.",
                code=ErrorCode.INTERNAL_SERVER_ERROR,
                details=None,
            ),
        )
