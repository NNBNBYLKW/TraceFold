from __future__ import annotations

from fastapi import FastAPI
from fastapi.testclient import TestClient
from alembic.util.exc import CommandError

from app.core.exceptions import (
    DatabaseUnavailableError,
    MigrationStateError,
    register_exception_handlers,
)


def _build_test_app() -> FastAPI:
    app = FastAPI()
    register_exception_handlers(app)

    @app.get("/db-unavailable")
    def db_unavailable() -> None:
        raise DatabaseUnavailableError(details={"operation": "runtime_status"})

    @app.get("/migration-state")
    def migration_state() -> None:
        raise MigrationStateError(details={"operation": "upgrade_database"})

    @app.get("/command-error")
    def command_error() -> None:
        raise CommandError("Revision head is not reachable.")

    @app.get("/unexpected")
    def unexpected() -> None:
        raise RuntimeError("sensitive internal detail")

    @app.get("/validation/{item_id}")
    def validation(item_id: int) -> dict[str, int]:
        return {"item_id": item_id}

    return app


def test_database_unavailable_error_uses_uniform_payload() -> None:
    client = TestClient(_build_test_app())

    response = client.get("/db-unavailable")

    assert response.status_code == 503
    assert response.json() == {
        "success": False,
        "message": "Database is unavailable.",
        "data": None,
        "meta": None,
        "error": {
            "code": "DATABASE_UNAVAILABLE",
            "details": {"operation": "runtime_status"},
            "retryable": True,
        },
    }


def test_migration_state_error_uses_uniform_payload() -> None:
    client = TestClient(_build_test_app())

    response = client.get("/migration-state")

    assert response.status_code == 503
    assert response.json() == {
        "success": False,
        "message": "Migration state is invalid.",
        "data": None,
        "meta": None,
        "error": {
            "code": "MIGRATION_STATE_ERROR",
            "details": {"operation": "upgrade_database"},
            "retryable": False,
        },
    }


def test_raw_command_error_is_mapped_to_migration_state_payload() -> None:
    client = TestClient(_build_test_app(), raise_server_exceptions=False)

    response = client.get("/command-error")

    assert response.status_code == 503
    body = response.json()
    assert body["success"] is False
    assert body["message"] == "Migration state is invalid."
    assert body["error"]["code"] == "MIGRATION_STATE_ERROR"
    assert body["error"]["retryable"] is False
    assert "Revision head is not reachable." in body["error"]["details"]["error"]


def test_request_validation_error_uses_uniform_payload() -> None:
    client = TestClient(_build_test_app())

    response = client.get("/validation/not-an-int")

    assert response.status_code == 400
    body = response.json()
    assert body["success"] is False
    assert body["message"] == "Validation failed."
    assert body["error"]["code"] == "VALIDATION_ERROR"
    assert isinstance(body["error"]["details"], list)
    assert "request_id" not in body["error"]
    assert "retryable" not in body["error"]


def test_unexpected_error_hides_internal_details() -> None:
    client = TestClient(_build_test_app(), raise_server_exceptions=False)

    response = client.get("/unexpected")

    assert response.status_code == 500
    assert response.json() == {
        "success": False,
        "message": "Internal server error.",
        "data": None,
        "meta": None,
        "error": {
            "code": "INTERNAL_SERVER_ERROR",
            "details": None,
        },
    }
    assert "sensitive internal detail" not in response.text
