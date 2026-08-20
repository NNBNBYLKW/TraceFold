from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Protocol


PACKAGING_ROOT = (
    Path(__file__).resolve().parents[1] / "packaging" / "windows-test"
)
sys.path.insert(0, str(PACKAGING_ROOT))

from launch_test_app import (  # noqa: E402
    LaunchError,
    assert_port_available,
    stop_owned_process,
    wait_for_health,
    wait_for_owned_window_close,
    wait_for_port_release,
)
from common import (  # noqa: E402
    PrerequisiteError,
    find_edge,
    required_command,
)


class OwnedProcess(Protocol):
    pid: int

    def poll(self) -> int | None: ...

    def terminate(self) -> None: ...

    def kill(self) -> None: ...

    def wait(self, timeout: float | None = None) -> int: ...


@dataclass(frozen=True)
class SourcePaths:
    root: Path
    api: Path
    web: Path
    data: Path
    runtime: Path
    logs: Path
    edge_profile: Path

    @classmethod
    def from_script(cls, script_file: Path) -> SourcePaths:
        root = script_file.resolve().parents[1]
        sandbox = root / ".source-test"
        runtime = sandbox / "runtime"
        return cls(
            root=root,
            api=root / "apps" / "media-api",
            web=root / "apps" / "media-web",
            data=sandbox / "data",
            runtime=runtime,
            logs=runtime / "logs",
            edge_profile=runtime / "edge-profile",
        )


def api_environment(
    paths: SourcePaths,
    *,
    base: Mapping[str, str] | None = None,
) -> dict[str, str]:
    environment = dict(os.environ if base is None else base)
    existing_pythonpath = environment.get("PYTHONPATH")
    pythonpath = str(paths.api / "src")
    if existing_pythonpath:
        pythonpath = os.pathsep.join((pythonpath, existing_pythonpath))
    environment["PYTHONPATH"] = pythonpath
    environment.pop("TRACEFOLD_MEDIA_WEB_DIST_DIR", None)
    environment.update(
        {
            "TRACEFOLD_MEDIA_INBOX_DIR": str(paths.data / "inbox"),
            "TRACEFOLD_MEDIA_LIBRARY_DIR": str(paths.data / "library"),
            "TRACEFOLD_MEDIA_WORK_DIR": str(paths.data / "work"),
            "TRACEFOLD_MEDIA_DATABASE_PATH": str(
                paths.data / "media.sqlite3"
            ),
            "TRACEFOLD_MEDIA_SCAN_INTERVAL_SECONDS": "2",
            "TRACEFOLD_MEDIA_WEB_ORIGIN": "http://127.0.0.1:3100",
        }
    )
    return environment


def web_environment(
    _paths: SourcePaths,
    *,
    base: Mapping[str, str] | None = None,
) -> dict[str, str]:
    environment = dict(os.environ if base is None else base)
    environment["VITE_API_BASE_URL"] = "http://127.0.0.1:8100/api"
    return environment


def migration_command(python: Path, paths: SourcePaths) -> list[str]:
    return [
        str(python),
        "-m",
        "alembic",
        "-x",
        f"database_path={paths.data / 'media.sqlite3'}",
        "upgrade",
        "head",
    ]


def api_command(python: Path) -> list[str]:
    return [
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


def web_command(node: Path, paths: SourcePaths) -> list[str]:
    return [
        str(node),
        str(paths.web / "node_modules" / "vite" / "bin" / "vite.js"),
        "--host",
        "127.0.0.1",
        "--port",
        "3100",
        "--strictPort",
    ]


def edge_command(edge: Path, paths: SourcePaths) -> list[str]:
    return [
        str(edge),
        "--app=http://127.0.0.1:3100",
        f"--user-data-dir={paths.edge_profile}",
        "--no-first-run",
        "--no-default-browser-check",
        "--edge-skip-compat-layer-relaunch",
        "--disable-background-mode",
    ]


def validate_source_tree(paths: SourcePaths) -> None:
    if not (paths.api / "pyproject.toml").is_file():
        raise LaunchError(f"未找到媒体 API 源码：{paths.api}")
    if not (paths.web / "package.json").is_file():
        raise LaunchError(f"未找到媒体 Web 源码：{paths.web}")
    vite = paths.web / "node_modules" / "vite" / "bin" / "vite.js"
    if not vite.is_file():
        raise LaunchError(
            "尚未安装前端依赖，请先在 apps/media-web 运行 npm install。"
        )


def wait_for_web(
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
            raise LaunchError(f"前端服务启动失败，退出代码：{returncode}")
        try:
            with open_url(url, timeout=1) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(0.1)
    raise LaunchError("前端服务启动超时，请查看运行日志。")


def stop_owned_processes(
    processes: Sequence[OwnedProcess | None],
) -> list[Exception]:
    errors: list[Exception] = []
    for process in reversed(processes):
        if process is not None:
            try:
                stop_owned_process(process)
            except Exception as error:
                errors.append(error)
    return errors


def stop_owned_process_tree(
    process: OwnedProcess,
    *,
    run_command=subprocess.run,
) -> None:
    if process.poll() is not None:
        return
    command = [
        "taskkill.exe",
        "/PID",
        str(process.pid),
        "/T",
        "/F",
    ]
    result = run_command(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    if result.returncode != 0:
        if process.poll() is not None:
            return
        try:
            stop_owned_process(process)
        finally:
            raise LaunchError(
                f"无法结束自有 Edge 进程树（PID {process.pid}）。"
            )
    process.wait(timeout=10)


def cleanup_source_session(
    api_process: OwnedProcess | None,
    web_process: OwnedProcess | None,
    edge_process: OwnedProcess | None,
    *,
    stop_tree=stop_owned_process_tree,
    wait_port_release=wait_for_port_release,
) -> None:
    errors: list[Exception] = []
    if edge_process is not None:
        try:
            stop_tree(edge_process)
        except Exception as error:
            errors.append(error)
    errors.extend(stop_owned_processes((api_process, web_process)))
    for port in (3100, 8100):
        try:
            wait_port_release("127.0.0.1", port, 15)
        except Exception as error:
            errors.append(error)
    if errors:
        details = "; ".join(str(error) for error in errors)
        raise LaunchError(f"源码测试进程清理失败：{details}")


def run_source_session(
    paths: SourcePaths,
    *,
    python: Path,
    node: Path,
    edge: Path | None,
    verify_only: bool = False,
    run_command=subprocess.run,
    start_process=subprocess.Popen,
    wait_api=wait_for_health,
    wait_web=wait_for_web,
    wait_window=wait_for_owned_window_close,
    stop_tree=stop_owned_process_tree,
    wait_port_release=wait_for_port_release,
) -> int:
    for directory in (
        paths.data / "inbox",
        paths.data / "library",
        paths.data / "work",
        paths.edge_profile,
        paths.logs,
    ):
        directory.mkdir(parents=True, exist_ok=True)

    api_env = api_environment(paths)
    web_env = web_environment(paths)
    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    log_path = paths.logs / f"source-test-{timestamp}.log"
    creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    api_process: OwnedProcess | None = None
    web_process: OwnedProcess | None = None
    edge_process: OwnedProcess | None = None

    with log_path.open("ab") as log:
        migration = run_command(
            migration_command(python, paths),
            cwd=paths.api,
            env=api_env,
            stdout=log,
            stderr=subprocess.STDOUT,
            check=False,
        )
        if migration.returncode != 0:
            raise LaunchError(f"数据库迁移失败，请查看日志：{log_path}")

        try:
            api_process = start_process(
                api_command(python),
                cwd=paths.api,
                env=api_env,
                stdout=log,
                stderr=subprocess.STDOUT,
                creationflags=creation_flags,
            )
            wait_api(
                "http://127.0.0.1:8100/health",
                api_process,
                timeout_seconds=30,
            )

            web_process = start_process(
                web_command(node, paths),
                cwd=paths.web,
                env=web_env,
                stdout=log,
                stderr=subprocess.STDOUT,
                creationflags=creation_flags,
            )
            wait_web(
                "http://127.0.0.1:3100",
                web_process,
                timeout_seconds=30,
            )

            if verify_only:
                return 0
            if edge is None:
                raise LaunchError("未找到 Microsoft Edge。")
            edge_process = start_process(edge_command(edge, paths))
            return wait_window(edge_process)
        finally:
            cleanup_source_session(
                api_process,
                web_process,
                edge_process,
                stop_tree=stop_tree,
                wait_port_release=wait_port_release,
            )


def launch(paths: SourcePaths, *, verify_only: bool = False) -> int:
    validate_source_tree(paths)
    assert_port_available("127.0.0.1", 8100)
    assert_port_available("127.0.0.1", 3100)
    required_command("ffmpeg")
    required_command("ffprobe")
    node = required_command("node.exe")
    edge = None if verify_only else find_edge(os.environ)
    if not verify_only and edge is None:
        raise PrerequisiteError("未找到 Microsoft Edge，请先安装 Edge。")
    return run_source_session(
        paths,
        python=Path(sys.executable).resolve(),
        node=node,
        edge=edge,
        verify_only=verify_only,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Launch TraceFold directly from the source checkout."
    )
    parser.add_argument(
        "--verify-only",
        action="store_true",
        help="start and verify both services without opening Edge",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    paths = SourcePaths.from_script(Path(__file__))
    try:
        return launch(paths, verify_only=args.verify_only)
    except (LaunchError, PrerequisiteError, OSError) as error:
        print(f"TraceFold 源码测试版启动失败：{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
