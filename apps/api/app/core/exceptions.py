from __future__ import annotations

from typing import Any

from fastapi import FastAPI, HTTPException, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app.core.responses import error_response


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
        code: str = "APP_ERROR",
        status_code: int = status.HTTP_400_BAD_REQUEST,
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        self.message = message
        self.code = code
        self.status_code = status_code
        self.details = details
        super().__init__(message)


class NotFoundError(AppException):
    def __init__(
        self,
        *,
        message: str = "Resource not found.",
        code: str = "NOT_FOUND",
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
        code: str = "CONFLICT",
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_409_CONFLICT,
            details=details,
        )


class BadRequestError(AppException):
    def __init__(
        self,
        *,
        message: str = "Bad request.",
        code: str = "BAD_REQUEST",
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
        code: str = "UNAUTHORIZED",
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
        code: str = "FORBIDDEN",
        details: dict[str, Any] | list[Any] | str | None = None,
    ) -> None:
        super().__init__(
            message=message,
            code=code,
            status_code=status.HTTP_403_FORBIDDEN,
            details=details,
        )


def register_exception_handlers(app: FastAPI) -> None:
    """
    Register global exception handlers.

    This should be called once during app startup / app creation.
    """

    @app.exception_handler(AppException)
    async def handle_app_exception(_, exc: AppException) -> JSONResponse:
        return JSONResponse(
            status_code=exc.status_code,
            content=error_response(
                message=exc.message,
                code=exc.code,
                details=exc.details,
            ).model_dump(),
        )

    @app.exception_handler(RequestValidationError)
    async def handle_validation_exception(
        _, exc: RequestValidationError
    ) -> JSONResponse:
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content=error_response(
                message="Validation failed.",
                code="VALIDATION_ERROR",
                details=exc.errors(),
            ).model_dump(),
        )

    @app.exception_handler(HTTPException)
    async def handle_http_exception(_, exc: HTTPException) -> JSONResponse:
        detail: Any = exc.detail

        if isinstance(detail, dict) and {"message", "code"} <= detail.keys():
            message = str(detail["message"])
            code = str(detail["code"])
            details = detail.get("details")
        else:
            message = str(detail) if detail is not None else "HTTP error."
            code = "HTTP_ERROR"
            details = None

        return JSONResponse(
            status_code=exc.status_code,
            content=error_response(
                message=message,
                code=code,
                details=details,
            ).model_dump(),
        )

    @app.exception_handler(Exception)
    async def handle_unexpected_exception(_, exc: Exception) -> JSONResponse:
        return JSONResponse(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            content=error_response(
                message="Internal server error.",
                code="INTERNAL_SERVER_ERROR",
                details=str(exc),
            ).model_dump(),
        )