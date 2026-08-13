from __future__ import annotations

import os
import platform
import shutil
import struct
import sys
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from pathlib import Path


class PrerequisiteError(RuntimeError):
    pass


@dataclass(frozen=True)
class PackagePaths:
    root: Path
    app: Path
    wheelhouse: Path
    venv: Path
    data: Path
    runtime: Path
    logs: Path

    @property
    def venv_python(self) -> Path:
        return self.venv / "Scripts" / "python.exe"

    @property
    def database(self) -> Path:
        return self.data / "media.sqlite3"

    @property
    def web(self) -> Path:
        return self.app / "web"

    @classmethod
    def from_script(cls, script_file: Path) -> PackagePaths:
        root = script_file.resolve().parents[1]
        return cls(
            root=root,
            app=root / "app",
            wheelhouse=root / "wheelhouse",
            venv=root / ".venv",
            data=root / "data",
            runtime=root / "runtime",
            logs=root / "runtime" / "logs",
        )


def validate_current_python() -> None:
    validate_python(
        (sys.version_info.major, sys.version_info.minor, sys.version_info.micro),
        pointer_bits=struct.calcsize("P") * 8,
        machine=platform.machine(),
    )


def validate_python(
    version: tuple[int, int, int],
    *,
    pointer_bits: int,
    machine: str,
) -> None:
    if (
        version[:2] != (3, 12)
        or pointer_bits != 64
        or machine.casefold() not in {"amd64", "x86_64"}
    ):
        raise PrerequisiteError("测试版需要 Python 3.12 x64。")


def find_edge(
    environment: Mapping[str, str],
    *,
    which: Callable[[str], str | None] = shutil.which,
) -> Path | None:
    located = which("msedge.exe")
    candidates = [Path(located)] if located else []
    candidates.extend(
        Path(environment[key])
        / "Microsoft"
        / "Edge"
        / "Application"
        / "msedge.exe"
        for key in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA")
        if environment.get(key)
    )
    return next((path.resolve() for path in candidates if path.is_file()), None)


def required_command(
    name: str,
    *,
    which: Callable[[str], str | None] = shutil.which,
) -> Path:
    located = which(name)
    if located is None:
        raise PrerequisiteError(f"未找到 {name}，请安装并加入 PATH。")
    return Path(located).resolve()


def runtime_environment(
    paths: PackagePaths,
    *,
    base: Mapping[str, str] | None = None,
) -> dict[str, str]:
    environment = dict(os.environ if base is None else base)
    environment.update(
        {
            "TRACEFOLD_MEDIA_INBOX_DIR": str((paths.data / "inbox").resolve()),
            "TRACEFOLD_MEDIA_LIBRARY_DIR": str(
                (paths.data / "library").resolve()
            ),
            "TRACEFOLD_MEDIA_WORK_DIR": str((paths.data / "work").resolve()),
            "TRACEFOLD_MEDIA_DATABASE_PATH": str(paths.database.resolve()),
            "TRACEFOLD_MEDIA_WEB_DIST_DIR": str(paths.web.resolve()),
        }
    )
    return environment
