from __future__ import annotations

from datetime import datetime

import pytest

from app.core.exceptions import DatabaseUnavailableError, MigrationStateError
from app.core.runtime_status import get_runtime_status_read


def test_runtime_status_reports_ok_when_db_and_schema_are_ready(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    checked_at = datetime(2026, 3, 23, 11, 30, 0)

    monkeypatch.setattr("app.core.runtime_status._utcnow", lambda: checked_at)
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0005")
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {"task_runtime_status": "ready", "degraded_reasons": [], "failed_count": 0},
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": [], "failed_count": 0},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "ok"
    assert status_read.db_status == "ok"
    assert status_read.migration_status == "ok"
    assert status_read.schema_version == "20260323_0005"
    assert status_read.migration_head == "20260323_0005"
    assert status_read.degraded_reasons == []
    assert status_read.task_runtime_status == "ready"
    assert status_read.last_checked_at == checked_at


def test_runtime_status_reports_missing_schema_version_as_failed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: None)
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {"task_runtime_status": "ready", "degraded_reasons": [], "failed_count": 0},
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": [], "failed_count": 0},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.db_status == "ok"
    assert status_read.migration_status == "failed"
    assert status_read.degraded_reasons == ["schema_not_initialized"]


def test_runtime_status_reports_outdated_when_schema_is_not_at_head(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0004")
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {"task_runtime_status": "ready", "degraded_reasons": [], "failed_count": 0},
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": [], "failed_count": 0},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.migration_status == "outdated"
    assert status_read.degraded_reasons == ["migration_not_at_head"]


def test_runtime_status_reports_database_unavailable_as_degraded(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "app.core.runtime_status._check_database",
        lambda: (_ for _ in ()).throw(DatabaseUnavailableError()),
    )
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.db_status == "unavailable"
    assert status_read.migration_status == "unknown"
    assert status_read.schema_version is None
    assert status_read.task_runtime_status == "degraded"
    assert status_read.degraded_reasons == ["database_unavailable"]


def test_runtime_status_reports_migration_head_failure_as_degraded(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr(
        "app.core.runtime_status.get_head_revision",
        lambda: (_ for _ in ()).throw(MigrationStateError()),
    )
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0005")
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {"task_runtime_status": "ready", "degraded_reasons": [], "failed_count": 0},
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": [], "failed_count": 0},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.db_status == "ok"
    assert status_read.schema_version == "20260323_0005"
    assert status_read.migration_status == "failed"
    assert status_read.degraded_reasons == ["migration_head_unavailable"]


def test_runtime_status_reports_degraded_when_task_runtime_has_failed_runs(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0005")
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {
            "task_runtime_status": "degraded",
            "degraded_reasons": ["task_runs_failed_present"],
            "failed_count": 1,
        },
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": [], "failed_count": 0},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.task_runtime_status == "degraded"
    assert status_read.degraded_reasons == ["task_runs_failed_present"]


def test_runtime_status_reports_derivation_failures_as_degraded_reason(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr("app.core.runtime_status._check_database", lambda: None)
    monkeypatch.setattr("app.core.runtime_status.get_head_revision", lambda: "20260323_0005")
    monkeypatch.setattr("app.core.runtime_status.get_current_revision", lambda: "20260323_0005")
    monkeypatch.setattr(
        "app.core.runtime_status._get_task_runtime_summary",
        lambda: {"task_runtime_status": "ready", "degraded_reasons": [], "failed_count": 0},
    )
    monkeypatch.setattr(
        "app.core.runtime_status._get_ai_derivation_summary",
        lambda: {"degraded_reasons": ["ai_derivations_failed_present"], "failed_count": 1},
    )

    status_read = get_runtime_status_read()

    assert status_read.api_status == "degraded"
    assert status_read.task_runtime_status == "ready"
    assert status_read.degraded_reasons == ["ai_derivations_failed_present"]
