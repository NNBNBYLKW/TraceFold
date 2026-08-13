from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

from common import (
    PackagePaths,
    PrerequisiteError,
    find_edge,
    required_command,
    runtime_environment,
    validate_current_python,
)


def offline_install_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python),
        "-m",
        "pip",
        "install",
        "--no-index",
        "--find-links",
        str(paths.wheelhouse),
        "--require-hashes",
        "-r",
        str(paths.app / "requirements-win-py312.lock"),
    ]


def migration_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python),
        "-m",
        "alembic",
        "-c",
        str(paths.app / "alembic.ini"),
        "upgrade",
        "head",
    ]


def install(paths: PackagePaths) -> None:
    validate_current_python()
    required_command("ffmpeg")
    required_command("ffprobe")
    if find_edge(os.environ) is None:
        raise PrerequisiteError("未找到 Microsoft Edge，请先安装 Edge。")

    for directory in (
        paths.data / "inbox",
        paths.data / "library",
        paths.data / "work",
        paths.runtime / "edge-profile",
        paths.logs,
    ):
        directory.mkdir(parents=True, exist_ok=True)

    if not paths.venv_python.is_file():
        subprocess.run(
            [sys.executable, "-m", "venv", str(paths.venv)],
            check=True,
        )

    environment = runtime_environment(paths)
    subprocess.run(
        offline_install_command(paths),
        cwd=paths.root,
        env=environment,
        check=True,
    )
    subprocess.run(
        migration_command(paths),
        cwd=paths.app,
        env=environment,
        check=True,
    )


def main() -> int:
    paths = PackagePaths.from_script(Path(__file__))
    try:
        install(paths)
    except (PrerequisiteError, OSError, subprocess.CalledProcessError) as error:
        print(f"安装失败：{error}", file=sys.stderr)
        print(f"日志目录：{paths.logs}", file=sys.stderr)
        return 1
    print("TraceFold 测试环境安装完成。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
