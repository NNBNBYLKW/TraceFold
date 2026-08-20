from __future__ import annotations

import importlib.util
import subprocess
import sys
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[5]
SOURCE_LAUNCHER = REPO_ROOT / "scripts" / "launch_source_test_app.py"


class FakeProcess:
    def __init__(self, returncode: int | None = None) -> None:
        self.pid = 4321
        self.returncode = returncode
        self.terminate_calls = 0

    def poll(self) -> int | None:
        return self.returncode

    def terminate(self) -> None:
        self.terminate_calls += 1

    def kill(self) -> None:
        self.returncode = -9

    def wait(self, timeout: float | None = None) -> int:
        self.returncode = 0 if self.returncode is None else self.returncode
        return self.returncode


class HtmlResponse:
    status = 200

    def __enter__(self):
        return self

    def __exit__(self, *_args) -> None:
        return None


class FakeStarter:
    def __init__(self, processes: list[FakeProcess]) -> None:
        self.remaining = list(processes)

    def __call__(self, *_args, **_kwargs) -> FakeProcess:
        if not self.remaining:
            raise AssertionError("started an unexpected process")
        return self.remaining.pop(0)


class TaskkillRunner:
    def __init__(self, process: FakeProcess) -> None:
        self.process = process
        self.command: list[str] | None = None

    def __call__(self, command: list[str], **_kwargs):
        self.command = command
        self.process.returncode = 0
        return subprocess.CompletedProcess(command, 0)


def load_source_launcher():
    if not SOURCE_LAUNCHER.is_file():
        pytest.fail("source test launcher has not been implemented")
    spec = importlib.util.spec_from_file_location(
        "tracefold_source_test_launcher",
        SOURCE_LAUNCHER,
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def test_runtime_environment_is_isolated_from_repository_media_data(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    script = tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    paths = launcher.SourcePaths.from_script(script)

    environment = launcher.api_environment(
        paths,
        base={"PATH": "existing-path", "PYTHONPATH": "existing-python-path"},
    )

    assert environment == {
        "PATH": "existing-path",
        "PYTHONPATH": (
            f"{paths.api / 'src'};existing-python-path"
        ),
        "TRACEFOLD_MEDIA_INBOX_DIR": str(paths.data / "inbox"),
        "TRACEFOLD_MEDIA_LIBRARY_DIR": str(paths.data / "library"),
        "TRACEFOLD_MEDIA_WORK_DIR": str(paths.data / "work"),
        "TRACEFOLD_MEDIA_DATABASE_PATH": str(paths.data / "media.sqlite3"),
        "TRACEFOLD_MEDIA_SCAN_INTERVAL_SECONDS": "2",
        "TRACEFOLD_MEDIA_WEB_ORIGIN": "http://127.0.0.1:3100",
    }
    assert paths.data.parent.name == ".source-test"
    assert paths.data != paths.root / "data"


def test_source_commands_run_current_code_without_building_a_package(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    script = tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    paths = launcher.SourcePaths.from_script(script)
    python = Path("C:/Python/python.exe")
    node = Path("C:/Program Files/nodejs/node.exe")

    assert launcher.migration_command(python, paths) == [
        str(python),
        "-m",
        "alembic",
        "-x",
        f"database_path={paths.data / 'media.sqlite3'}",
        "upgrade",
        "head",
    ]
    assert launcher.api_command(python) == [
        str(python),
        "-m",
        "uvicorn",
        "media_api.main:create_app",
        "--factory",
        "--host",
        "127.0.0.1",
        "--port",
        "8100",
    ]
    assert launcher.web_command(node, paths) == [
        str(node),
        str(paths.web / "node_modules" / "vite" / "bin" / "vite.js"),
        "--host",
        "127.0.0.1",
        "--port",
        "3100",
        "--strictPort",
    ]
    assert launcher.web_environment(paths, base={"PATH": "existing"}) == {
        "PATH": "existing",
        "VITE_API_BASE_URL": "http://127.0.0.1:8100/api",
    }


def test_edge_uses_an_owned_source_profile_and_app_window(tmp_path: Path) -> None:
    launcher = load_source_launcher()
    script = tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    paths = launcher.SourcePaths.from_script(script)
    edge = Path("C:/Program Files/Microsoft/Edge/Application/msedge.exe")

    assert launcher.edge_command(edge, paths) == [
        str(edge),
        "--app=http://127.0.0.1:3100",
        f"--user-data-dir={paths.edge_profile}",
        "--no-first-run",
        "--no-default-browser-check",
        "--edge-skip-compat-layer-relaunch",
        "--disable-background-mode",
    ]


def test_web_wait_fails_when_the_owned_vite_process_exits() -> None:
    launcher = load_source_launcher()

    with pytest.raises(launcher.LaunchError, match="前端服务启动失败"):
        launcher.wait_for_web(
            "http://127.0.0.1:3100",
            FakeProcess(returncode=3),
            timeout_seconds=1,
            open_url=lambda *_args, **_kwargs: HtmlResponse(),
        )


def test_web_wait_accepts_a_successful_page_response() -> None:
    launcher = load_source_launcher()

    launcher.wait_for_web(
        "http://127.0.0.1:3100",
        FakeProcess(),
        timeout_seconds=1,
        open_url=lambda *_args, **_kwargs: HtmlResponse(),
    )


def test_cleanup_stops_every_owned_source_process() -> None:
    launcher = load_source_launcher()
    api = FakeProcess()
    web = FakeProcess()
    edge = FakeProcess()

    launcher.stop_owned_processes((api, web, edge))

    assert api.terminate_calls == 1
    assert web.terminate_calls == 1
    assert edge.terminate_calls == 1
    assert api.poll() == 0
    assert web.poll() == 0
    assert edge.poll() == 0


def test_edge_tree_cleanup_targets_the_live_owned_root_pid() -> None:
    launcher = load_source_launcher()
    edge = FakeProcess()
    runner = TaskkillRunner(edge)

    launcher.stop_owned_process_tree(edge, run_command=runner)

    assert runner.command == [
        "taskkill.exe",
        "/PID",
        str(edge.pid),
        "/T",
        "/F",
    ]
    assert edge.poll() == 0


def test_cleanup_failure_does_not_skip_other_processes_or_port_checks() -> None:
    launcher = load_source_launcher()
    api = FakeProcess()
    web = FakeProcess()
    edge = FakeProcess()
    checked_ports: list[int] = []

    def fail_edge_tree(_process) -> None:
        raise OSError("tree cleanup failed")

    with pytest.raises(launcher.LaunchError, match="清理失败"):
        launcher.cleanup_source_session(
            api,
            web,
            edge,
            stop_tree=fail_edge_tree,
            wait_port_release=lambda _host, port, _timeout: (
                checked_ports.append(port)
            ),
        )

    assert api.terminate_calls == 1
    assert web.terminate_calls == 1
    assert checked_ports == [3100, 8100]


def test_source_session_stops_all_services_after_the_app_window_closes(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    paths = launcher.SourcePaths.from_script(
        tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    )
    api = FakeProcess()
    web = FakeProcess()
    edge = FakeProcess()
    starter = FakeStarter([api, web, edge])

    result = launcher.run_source_session(
        paths,
        python=Path("C:/Python/python.exe"),
        node=Path("C:/node/node.exe"),
        edge=Path("C:/Edge/msedge.exe"),
        run_command=lambda *_args, **_kwargs: subprocess.CompletedProcess(
            [], 0
        ),
        start_process=starter,
        wait_api=lambda *_args, **_kwargs: None,
        wait_web=lambda *_args, **_kwargs: None,
        wait_window=lambda *_args, **_kwargs: 0,
        stop_tree=launcher.stop_owned_process,
        wait_port_release=lambda *_args, **_kwargs: None,
    )

    assert result == 0
    assert starter.remaining == []
    assert api.terminate_calls == 1
    assert web.terminate_calls == 1
    assert edge.terminate_calls == 1


def test_verify_only_checks_source_services_without_opening_edge(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    paths = launcher.SourcePaths.from_script(
        tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    )
    api = FakeProcess()
    web = FakeProcess()
    starter = FakeStarter([api, web])

    result = launcher.run_source_session(
        paths,
        python=Path("C:/Python/python.exe"),
        node=Path("C:/node/node.exe"),
        edge=None,
        verify_only=True,
        run_command=lambda *_args, **_kwargs: subprocess.CompletedProcess(
            [], 0
        ),
        start_process=starter,
        wait_api=lambda *_args, **_kwargs: None,
        wait_web=lambda *_args, **_kwargs: None,
        wait_window=lambda *_args, **_kwargs: pytest.fail(
            "verify-only must not wait for an Edge window"
        ),
        stop_tree=launcher.stop_owned_process,
        wait_port_release=lambda *_args, **_kwargs: None,
    )

    assert result == 0
    assert starter.remaining == []
    assert api.terminate_calls == 1
    assert web.terminate_calls == 1


def test_source_tree_check_explains_that_frontend_dependencies_are_missing(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    paths = launcher.SourcePaths.from_script(
        tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    )
    paths.api.mkdir(parents=True)
    paths.web.mkdir(parents=True)
    (paths.api / "pyproject.toml").write_text("", encoding="utf-8")
    (paths.web / "package.json").write_text("{}", encoding="utf-8")

    with pytest.raises(launcher.LaunchError, match="npm install"):
        launcher.validate_source_tree(paths)


def test_source_tree_check_accepts_an_installed_source_checkout(
    tmp_path: Path,
) -> None:
    launcher = load_source_launcher()
    paths = launcher.SourcePaths.from_script(
        tmp_path / "TraceFold" / "scripts" / "launch_source_test_app.py"
    )
    paths.api.mkdir(parents=True)
    (paths.api / "pyproject.toml").write_text("", encoding="utf-8")
    vite = paths.web / "node_modules" / "vite" / "bin" / "vite.js"
    vite.parent.mkdir(parents=True)
    vite.write_text("", encoding="utf-8")
    (paths.web / "package.json").write_text("{}", encoding="utf-8")

    launcher.validate_source_tree(paths)
