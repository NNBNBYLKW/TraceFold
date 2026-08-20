from __future__ import annotations

import ctypes
import json
import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Callable, Protocol

from common import (
    PackagePaths,
    PrerequisiteError,
    find_edge,
    required_command,
    runtime_environment,
)


class OwnedProcess(Protocol):
    pid: int

    def poll(self) -> int | None:
        raise NotImplementedError

    def terminate(self) -> None:
        raise NotImplementedError

    def kill(self) -> None:
        raise NotImplementedError

    def wait(self, timeout: float | None = None) -> int:
        raise NotImplementedError


class LaunchError(RuntimeError):
    pass


def assert_port_available(host: str, port: int) -> None:
    with socket.socket() as probe:
        try:
            probe.bind((host, port))
        except OSError as error:
            raise LaunchError(
                f"端口 {host}:{port} 已被其他程序占用。"
            ) from error


def wait_for_health(
    url: str,
    process: OwnedProcess,
    timeout_seconds: float,
    *,
    open_url=urllib.request.urlopen,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        returncode = process.poll()
        if returncode is not None:
            raise LaunchError(f"服务启动失败，退出代码：{returncode}")
        try:
            with open_url(url, timeout=1) as response:
                payload = json.loads(response.read())
                if response.status == 200 and payload.get("status") == "ok":
                    return
        except (OSError, ValueError, urllib.error.URLError):
            pass
        time.sleep(0.1)
    raise LaunchError("服务健康检查超时，请查看运行日志。")


def stop_owned_process(
    process: OwnedProcess,
    timeout_seconds: float = 10,
) -> None:
    if process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=timeout_seconds)


def wait_for_port_release(
    host: str,
    port: int,
    timeout_seconds: float,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        try:
            assert_port_available(host, port)
            return
        except LaunchError:
            time.sleep(0.1)
    raise LaunchError(f"服务退出后端口 {host}:{port} 未释放。")


def owned_visible_window_exists(process_id: int) -> bool:
    if os.name != "nt":
        raise LaunchError("测试版窗口监测仅支持 Windows。")

    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(
        wintypes.BOOL,
        wintypes.HWND,
        wintypes.LPARAM,
    )
    found = False

    @callback_type
    def inspect_window(window_handle, _parameter):
        nonlocal found
        if not user32.IsWindowVisible(window_handle):
            return True
        owner_process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(
            window_handle,
            ctypes.byref(owner_process_id),
        )
        if owner_process_id.value == process_id:
            found = True
            return False
        return True

    user32.EnumWindows(inspect_window, 0)
    return found


def wait_for_owned_window_close(
    process: OwnedProcess,
    startup_timeout_seconds: float = 15,
    *,
    window_visible: Callable[[int], bool] = owned_visible_window_exists,
    monotonic: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> int:
    startup_deadline = monotonic() + startup_timeout_seconds
    window_seen = False
    while True:
        returncode = process.poll()
        if returncode is not None:
            if window_seen and returncode == 0:
                return 0
            raise LaunchError(f"Edge 窗口退出代码：{returncode}")

        if window_visible(process.pid):
            window_seen = True
        elif window_seen:
            return 0
        elif monotonic() >= startup_deadline:
            raise LaunchError("Edge 窗口启动超时。")
        sleep(0.1)


def server_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python),
        "-m",
        "uvicorn",
        "media_api.main:create_app",
        "--factory",
        "--host",
        "127.0.0.1",
        "--port",
        "8100",
    ]


def edge_command(edge: Path, paths: PackagePaths) -> list[str]:
    return [
        str(edge),
        "--app=http://127.0.0.1:8100",
        f"--user-data-dir={paths.runtime / 'edge-profile'}",
        "--no-first-run",
        "--no-default-browser-check",
        "--edge-skip-compat-layer-relaunch",
        "--disable-background-mode",
    ]


def launch(paths: PackagePaths) -> int:
    if not paths.venv_python.is_file():
        raise LaunchError("尚未安装测试环境，请先运行安装测试环境.cmd。")
    required_command("ffmpeg")
    required_command("ffprobe")
    edge = find_edge(os.environ)
    if edge is None:
        raise PrerequisiteError("未找到 Microsoft Edge，请先安装 Edge。")

    for directory in (
        paths.data / "inbox",
        paths.data / "library",
        paths.data / "work",
        paths.runtime / "edge-profile",
        paths.logs,
    ):
        directory.mkdir(parents=True, exist_ok=True)
        if not os.access(directory, os.W_OK):
            raise LaunchError(f"目录不可写：{directory}")

    host = "127.0.0.1"
    port = 8100
    assert_port_available(host, port)
    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    log_path = paths.logs / f"tracefold-{timestamp}.log"
    creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    server: subprocess.Popen[bytes] | None = None
    edge_process: subprocess.Popen[bytes] | None = None
    with log_path.open("ab") as log:
        try:
            server = subprocess.Popen(
                server_command(paths),
                cwd=paths.app,
                env=runtime_environment(paths),
                stdout=log,
                stderr=subprocess.STDOUT,
                creationflags=creation_flags,
            )
            wait_for_health(
                f"http://{host}:{port}/health",
                server,
                timeout_seconds=30,
            )
            edge_process = subprocess.Popen(edge_command(edge, paths))
            wait_for_owned_window_close(edge_process)
        finally:
            try:
                if edge_process is not None:
                    stop_owned_process(edge_process)
            finally:
                if server is not None:
                    stop_owned_process(server)
                    wait_for_port_release(host, port, timeout_seconds=10)
    return 0


def main() -> int:
    paths = PackagePaths.from_script(Path(__file__))
    try:
        return launch(paths)
    except (LaunchError, PrerequisiteError, OSError) as error:
        print(f"启动失败：{error}", file=sys.stderr)
        print(f"日志目录：{paths.logs}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
