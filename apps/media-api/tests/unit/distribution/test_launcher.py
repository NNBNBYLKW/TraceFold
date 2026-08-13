from __future__ import annotations

import importlib.util
import socket
import subprocess
import sys
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[5]
PACKAGING_ROOT = REPO_ROOT / "packaging" / "windows-test"


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


common = load_module("tracefold_launcher_common", PACKAGING_ROOT / "common.py")
sys.modules["common"] = common
launcher = load_module(
    "tracefold_dist_launcher",
    PACKAGING_ROOT / "launch_test_app.py",
)

PackagePaths = common.PackagePaths
LaunchError = launcher.LaunchError
assert_port_available = launcher.assert_port_available
edge_command = launcher.edge_command
server_command = launcher.server_command
stop_owned_process = launcher.stop_owned_process
wait_for_health = launcher.wait_for_health


class FakeProcess:
    def __init__(
        self,
        *,
        returncode: int | None,
        wait_times_out: bool = False,
    ) -> None:
        self.returncode = returncode
        self.wait_times_out = wait_times_out
        self.terminate_calls = 0
        self.kill_calls = 0
        self.wait_calls = 0

    def poll(self) -> int | None:
        return self.returncode

    def terminate(self) -> None:
        self.terminate_calls += 1

    def kill(self) -> None:
        self.kill_calls += 1
        self.returncode = -9

    def wait(self, timeout: float | None = None) -> int:
        self.wait_calls += 1
        if self.wait_times_out and self.kill_calls == 0:
            raise subprocess.TimeoutExpired("owned", timeout)
        self.returncode = 0 if self.returncode is None else self.returncode
        return self.returncode


class JsonResponse:
    status = 200

    def __enter__(self):
        return self

    def __exit__(self, *_args) -> None:
        return None

    def read(self) -> bytes:
        return b'{"service":"tracefold-media-api","status":"ok"}'


@pytest.fixture
def package_paths(tmp_path: Path):
    script = tmp_path / "TraceFold-Test-0.1.0" / "app" / "launch_test_app.py"
    return PackagePaths.from_script(script)


def test_port_check_never_terminates_the_existing_owner() -> None:
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        host, port = listener.getsockname()

        with pytest.raises(LaunchError, match="端口"):
            assert_port_available(host, port)


def test_health_wait_stops_when_owned_server_exits() -> None:
    process = FakeProcess(returncode=3)

    with pytest.raises(LaunchError, match="启动失败"):
        wait_for_health(
            "http://127.0.0.1:8100/health",
            process,
            timeout_seconds=1,
            open_url=lambda *_args, **_kwargs: JsonResponse(),
        )


def test_health_wait_accepts_only_expected_payload() -> None:
    wait_for_health(
        "http://127.0.0.1:8100/health",
        FakeProcess(returncode=None),
        timeout_seconds=1,
        open_url=lambda *_args, **_kwargs: JsonResponse(),
    )


def test_cleanup_terminates_only_the_owned_process() -> None:
    owned = FakeProcess(returncode=None, wait_times_out=True)

    stop_owned_process(owned, timeout_seconds=0)

    assert owned.terminate_calls == 1
    assert owned.kill_calls == 1
    assert owned.wait_calls == 2


def test_cleanup_is_noop_after_owned_process_exits() -> None:
    owned = FakeProcess(returncode=0)

    stop_owned_process(owned)

    assert owned.terminate_calls == 0
    assert owned.kill_calls == 0


def test_server_and_edge_commands_are_package_scoped(package_paths) -> None:
    edge = Path("C:/Program Files/Microsoft/Edge/Application/msedge.exe")

    assert server_command(package_paths) == [
        str(package_paths.venv_python),
        "-m",
        "uvicorn",
        "media_api.main:create_app",
        "--factory",
        "--host",
        "127.0.0.1",
        "--port",
        "8100",
    ]
    assert edge_command(edge, package_paths) == [
        str(edge),
        "--app=http://127.0.0.1:8100",
        f"--user-data-dir={package_paths.runtime / 'edge-profile'}",
        "--no-first-run",
        "--no-default-browser-check",
    ]
