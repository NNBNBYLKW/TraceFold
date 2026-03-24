from __future__ import annotations

from typing import Any, Generic, TypeVar

from pydantic import BaseModel, ConfigDict, Field


T = TypeVar("T")


class ResponseMeta(BaseModel):
    """
    Optional metadata for API responses.

    Typical use cases:
    - pagination
    - total counts
    - lightweight extra context
    """

    model_config = ConfigDict(extra="forbid")

    page: int | None = None
    page_size: int | None = None
    total: int | None = None


class ErrorDetail(BaseModel):
    """
    Standardized error payload for failed responses.
    """

    model_config = ConfigDict(extra="forbid")

    code: str
    details: dict[str, Any] | list[Any] | str | None = None
    request_id: str | None = None
    retryable: bool | None = None


class ApiResponse(BaseModel, Generic[T]):
    """
    Standard API response envelope.

    Example success response:
    {
        "success": true,
        "message": "Capture record created.",
        "data": {...},
        "meta": null,
        "error": null
    }

    Example error response:
    {
        "success": false,
        "message": "Validation failed.",
        "data": null,
        "meta": null,
        "error": {
            "code": "VALIDATION_ERROR",
            "details": {...}
        }
    }
    """

    model_config = ConfigDict(extra="forbid")

    success: bool = Field(..., description="Whether the request succeeded.")
    message: str = Field(..., description="Human-readable response message.")
    data: T | None = Field(default=None, description="Response payload.")
    meta: ResponseMeta | None = Field(
        default=None,
        description="Optional metadata, e.g. pagination info.",
    )
    error: ErrorDetail | None = Field(
        default=None,
        description="Structured error info for failed responses.",
    )


def success_response(
    *,
    data: T | None = None,
    message: str = "OK",
    meta: ResponseMeta | None = None,
) -> ApiResponse[T]:
    """
    Build a standardized success response.
    """
    return ApiResponse[T](
        success=True,
        message=message,
        data=data,
        meta=meta,
        error=None,
    )


def created_response(
    *,
    data: T | None = None,
    message: str = "Created",
    meta: ResponseMeta | None = None,
) -> ApiResponse[T]:
    """
    Build a standardized creation success response.
    """
    return ApiResponse[T](
        success=True,
        message=message,
        data=data,
        meta=meta,
        error=None,
    )


def error_response(
    *,
    message: str = "Request failed",
    code: str = "REQUEST_FAILED",
    details: dict[str, Any] | list[Any] | str | None = None,
    request_id: str | None = None,
    retryable: bool | None = None,
    meta: ResponseMeta | None = None,
) -> ApiResponse[None]:
    """
    Build a standardized error response.

    This is mainly intended to be used by global exception handlers.
    """
    return ApiResponse[None](
        success=False,
        message=message,
        data=None,
        meta=meta,
        error=ErrorDetail(
            code=code,
            details=details,
            request_id=request_id,
            retryable=retryable,
        ),
    )
