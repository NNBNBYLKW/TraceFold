from apps.desktop.app.shell.window import MainWindowSkeleton


def test_open_workbench_returns_ready_for_valid_url():
    window = MainWindowSkeleton(
        title="TraceFold Desktop Shell",
        workbench_url="http://localhost:3000",
    )

    result = window.open_workbench()

    assert result.status == "ready"
    assert result.url == "http://localhost:3000"
    assert result.error is None
    assert window.load_state == "ready"


def test_open_workbench_returns_error_when_url_is_missing():
    window = MainWindowSkeleton(
        title="TraceFold Desktop Shell",
        workbench_url="",
    )

    result = window.open_workbench()

    assert result.status == "error"
    assert result.url is None
    assert result.error == "Workbench URL is not configured."
    assert window.load_state == "error"


def test_window_can_hold_minimal_service_status_view_state():
    window = MainWindowSkeleton(
        title="TraceFold Desktop Shell",
        workbench_url="http://localhost:3000",
    )

    snapshot = window.update_service_status(
        status="unavailable",
        error_hint="Cannot reach TraceFold API.",
    )

    assert snapshot["label"] == "Service: unavailable"
    assert snapshot["hint"] == "Cannot reach TraceFold API."
    assert window.service_status_label == "Service: unavailable"
