from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import tomllib
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass
from email.parser import BytesParser
from pathlib import Path, PurePosixPath
from typing import BinaryIO, Iterable, Sequence


TRACEFOLD_TEST_RELEASE_VERSION = "0.1.0"
PACKAGE_PREFIX = "TraceFold-Test"
ARCHIVE_SUFFIX = "win-x64"
FIXED_ZIP_TIMESTAMP = (2026, 1, 1, 0, 0, 0)


@dataclass(frozen=True)
class BuildLayout:
    repo_root: Path
    staging_root: Path
    package_root: Path
    artifact: Path

    @classmethod
    def for_repo(cls, repo_root: Path, version: str) -> "BuildLayout":
        if not re.fullmatch(r"\d+\.\d+\.\d+", version):
            raise ValueError("release version must use X.Y.Z format")
        resolved_root = repo_root.resolve()
        staging_root = resolved_root / "build" / "windows-test-package"
        package_name = f"{PACKAGE_PREFIX}-{version}"
        return cls(
            repo_root=resolved_root,
            staging_root=staging_root,
            package_root=staging_root / package_name,
            artifact=(
                resolved_root
                / "dist"
                / f"{package_name}-{ARCHIVE_SUFFIX}.zip"
            ),
        )

    @property
    def wheelhouse(self) -> Path:
        return self.package_root / "wheelhouse"

    @property
    def lockfile(self) -> Path:
        return self.package_root / "app" / "requirements-win-py312.lock"

    @property
    def checksum(self) -> Path:
        return self.artifact.with_suffix(self.artifact.suffix + ".sha256")

    @property
    def build_log(self) -> Path:
        return self.staging_root / "build.log"


@dataclass(frozen=True)
class ReleaseVerification:
    install_returncode: int
    health_payload: dict[str, str]
    frontend_status: int
    api_status: int
    port_released: bool
    install_log_contains_no_index: bool
    restart_preserved_data: bool
    network_download_detected: bool


def _is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
    except ValueError:
        return False
    return True


def reset_staging(layout: BuildLayout) -> None:
    repo_root = layout.repo_root.resolve()
    staging_root = layout.staging_root.resolve()
    expected = repo_root / "build" / "windows-test-package"
    package_root = layout.package_root.resolve()
    safe = (
        staging_root == expected
        and staging_root.parent == repo_root / "build"
        and package_root.parent == staging_root
        and package_root.name.startswith(f"{PACKAGE_PREFIX}-")
        and _is_relative_to(package_root, staging_root)
    )
    if not safe:
        raise ValueError(
            "refusing cleanup outside the exact build/windows-test-package directory"
        )
    if staging_root.exists():
        shutil.rmtree(staging_root)


def _copy_tree(source: Path, destination: Path) -> None:
    if not source.is_dir():
        raise FileNotFoundError(f"required directory is missing: {source}")
    shutil.copytree(
        source,
        destination,
        ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo"),
    )


def _copy_file(source: Path, destination: Path) -> None:
    if not source.is_file():
        raise FileNotFoundError(f"required file is missing: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def assemble(layout: BuildLayout) -> Path:
    root = layout.repo_root
    api = root / "apps" / "media-api"
    web = root / "apps" / "media-web"
    templates = root / "packaging" / "windows-test"
    package = layout.package_root
    app = package / "app"

    package.mkdir(parents=True, exist_ok=True)
    _copy_tree(api / "src" / "media_api", app / "media_api")
    _copy_tree(api / "migrations", app / "migrations")
    _copy_file(api / "alembic.ini", app / "alembic.ini")
    _copy_tree(web / "dist", app / "web")

    for name in ("common.py", "install_test_env.py", "launch_test_app.py"):
        _copy_file(templates / name, app / name)
    for name in ("安装测试环境.cmd", "启动 TraceFold.cmd", "测试版使用说明.txt"):
        _copy_file(templates / name, package / name)

    for relative in (
        Path("data/inbox"),
        Path("data/library"),
        Path("data/work"),
        Path("runtime/edge-profile"),
        Path("runtime/logs"),
    ):
        directory = package / relative
        directory.mkdir(parents=True, exist_ok=True)
        (directory / ".gitkeep").write_bytes(b"")

    layout.wheelhouse.mkdir(parents=True, exist_ok=True)
    return package


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _normalize_project_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def _wheel_identity(wheel: Path) -> tuple[str, str]:
    try:
        with zipfile.ZipFile(wheel) as archive:
            metadata_members = [
                member
                for member in archive.namelist()
                if member.endswith(".dist-info/METADATA")
            ]
            if len(metadata_members) != 1:
                raise ValueError(
                    f"wheel must contain exactly one METADATA file: {wheel.name}"
                )
            metadata = BytesParser().parsebytes(
                archive.read(metadata_members[0]),
                headersonly=True,
            )
    except zipfile.BadZipFile as error:
        raise ValueError(f"invalid wheel archive: {wheel.name}") from error
    name = metadata.get("Name")
    version = metadata.get("Version")
    if not name or not version:
        raise ValueError(f"wheel METADATA lacks Name or Version: {wheel.name}")
    return _normalize_project_name(name), version


def write_hashed_lock(wheelhouse: Path, destination: Path) -> None:
    files = sorted(path for path in wheelhouse.iterdir() if path.is_file())
    non_wheels = [path.name for path in files if path.suffix.lower() != ".whl"]
    if non_wheels:
        raise ValueError(
            "wheelhouse may contain wheel files only: " + ", ".join(non_wheels)
        )
    if not files:
        raise ValueError("wheelhouse is empty")

    locked: dict[str, tuple[str, str]] = {}
    for wheel in files:
        name, version = _wheel_identity(wheel)
        if name in locked:
            raise ValueError(f"duplicate normalized wheel project name: {name}")
        locked[name] = (version, _sha256(wheel))

    lines = [
        f"{name}=={version} --hash=sha256:{digest}\n"
        for name, (version, digest) in sorted(locked.items())
    ]
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text("".join(lines), encoding="utf-8", newline="\n")


def _mutable_payloads(package_root: Path) -> list[Path]:
    payloads: list[Path] = []
    for name in ("data", "runtime"):
        root = package_root / name
        if not root.exists():
            continue
        payloads.extend(
            path
            for path in root.rglob("*")
            if path.is_file() and path.name != ".gitkeep"
        )
    return payloads


def write_manifest(package_root: Path) -> Path:
    mutable = _mutable_payloads(package_root)
    if mutable:
        listed = ", ".join(
            path.relative_to(package_root).as_posix() for path in mutable
        )
        raise ValueError(f"mutable data must not enter the release: {listed}")

    destination = package_root / "MANIFEST.sha256"
    files = sorted(
        (
            path
            for path in package_root.rglob("*")
            if path.is_file() and path != destination
        ),
        key=lambda path: path.relative_to(package_root).as_posix(),
    )
    lines = [
        f"{_sha256(path)}  {path.relative_to(package_root).as_posix()}\n"
        for path in files
    ]
    destination.write_text("".join(lines), encoding="utf-8", newline="\n")
    return destination


def _read_project(repo_root: Path) -> tuple[str, list[str]]:
    pyproject = repo_root / "apps" / "media-api" / "pyproject.toml"
    with pyproject.open("rb") as source:
        project = tomllib.load(source)["project"]
    return str(project["version"]), [str(item) for item in project["dependencies"]]


def _run_logged(
    command: Sequence[str],
    *,
    cwd: Path,
    log: BinaryIO,
    env: dict[str, str] | None = None,
) -> None:
    resolved_command = [str(part) for part in command]
    located = shutil.which(resolved_command[0])
    if located is not None:
        resolved_command[0] = located
    rendered = subprocess.list2cmdline(resolved_command)
    header = f"\n> {rendered}\n".encode("utf-8")
    log.write(header)
    log.flush()
    print(rendered)
    process = subprocess.Popen(
        resolved_command,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    assert process.stdout is not None
    for block in iter(lambda: process.stdout.read(64 * 1024), b""):
        log.write(block)
        log.flush()
        sys.stdout.buffer.write(block)
        sys.stdout.buffer.flush()
    returncode = process.wait()
    if returncode != 0:
        raise subprocess.CalledProcessError(returncode, command)


def _validate_external_checksum(archive: Path) -> None:
    checksum = archive.with_suffix(archive.suffix + ".sha256")
    if not checksum.is_file():
        raise FileNotFoundError(f"archive checksum is missing: {checksum}")
    fields = checksum.read_text(encoding="utf-8").strip().split()
    if len(fields) != 2 or fields[1] != archive.name:
        raise ValueError("invalid external checksum file")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", fields[0]):
        raise ValueError("invalid external SHA-256 value")
    if _sha256(archive) != fields[0].lower():
        raise ValueError("archive SHA-256 mismatch")


def _safe_zip_members(archive: zipfile.ZipFile) -> tuple[list[str], str]:
    files: list[str] = []
    top_levels: set[str] = set()
    for member in archive.infolist():
        name = member.filename
        path = PurePosixPath(name)
        if (
            not name
            or "\\" in name
            or path.is_absolute()
            or ".." in path.parts
            or re.match(r"^[A-Za-z]:", name)
        ):
            raise ValueError(f"unsafe ZIP member: {name}")
        top_levels.add(path.parts[0])
        if not member.is_dir():
            files.append(name)
    if len(top_levels) != 1:
        raise ValueError("release ZIP must contain exactly one top-level directory")
    top_level = next(iter(top_levels))
    if not re.fullmatch(r"TraceFold-Test-\d+\.\d+\.\d+", top_level):
        raise ValueError(f"unexpected release directory: {top_level}")
    return files, top_level


def _verify_package_manifest(package_root: Path) -> None:
    manifest = package_root / "MANIFEST.sha256"
    if not manifest.is_file():
        raise FileNotFoundError("package manifest is missing")
    expected: set[str] = set()
    for line in manifest.read_text(encoding="utf-8").splitlines():
        try:
            digest, relative = line.split("  ", 1)
        except ValueError as error:
            raise ValueError("invalid package manifest line") from error
        path = PurePosixPath(relative)
        if (
            not re.fullmatch(r"[0-9a-f]{64}", digest)
            or path.is_absolute()
            or ".." in path.parts
            or "\\" in relative
        ):
            raise ValueError("invalid package manifest entry")
        target = package_root.joinpath(*path.parts)
        if not target.is_file() or _sha256(target) != digest:
            raise ValueError(f"package manifest mismatch: {relative}")
        expected.add(relative)
    actual = {
        path.relative_to(package_root).as_posix()
        for path in package_root.rglob("*")
        if path.is_file() and path != manifest
    }
    if actual != expected:
        raise ValueError("package manifest does not cover the extracted release")


def _runtime_environment(package_root: Path) -> dict[str, str]:
    environment = dict(os.environ)
    environment.update(
        {
            "TRACEFOLD_MEDIA_INBOX_DIR": str(
                (package_root / "data" / "inbox").resolve()
            ),
            "TRACEFOLD_MEDIA_LIBRARY_DIR": str(
                (package_root / "data" / "library").resolve()
            ),
            "TRACEFOLD_MEDIA_WORK_DIR": str(
                (package_root / "data" / "work").resolve()
            ),
            "TRACEFOLD_MEDIA_DATABASE_PATH": str(
                (package_root / "data" / "media.sqlite3").resolve()
            ),
            "TRACEFOLD_MEDIA_WEB_DIST_DIR": str(
                (package_root / "app" / "web").resolve()
            ),
        }
    )
    return environment


def _venv_python(package_root: Path) -> Path:
    return package_root / ".venv" / "Scripts" / "python.exe"


def _server_command(package_root: Path) -> list[str]:
    return [
        str(_venv_python(package_root)),
        "-m",
        "uvicorn",
        "media_api.main:create_app",
        "--factory",
        "--host",
        "127.0.0.1",
        "--port",
        "8100",
    ]


def _port_available(host: str = "127.0.0.1", port: int = 8100) -> bool:
    with socket.socket() as probe:
        try:
            probe.bind((host, port))
        except OSError:
            return False
    return True


def _wait_for_port_release(timeout_seconds: float = 15) -> bool:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if _port_available():
            return True
        time.sleep(0.1)
    return False


def _request_json(url: str) -> tuple[int, dict[str, str]]:
    with urllib.request.urlopen(url, timeout=2) as response:
        return response.status, json.loads(response.read())


def _wait_for_health(
    server: subprocess.Popen[bytes],
    timeout_seconds: float = 30,
) -> dict[str, str]:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        returncode = server.poll()
        if returncode is not None:
            raise RuntimeError(f"release API exited early with code {returncode}")
        try:
            status, payload = _request_json("http://127.0.0.1:8100/health")
            if status == 200 and payload.get("status") == "ok":
                return payload
        except (OSError, ValueError, urllib.error.URLError):
            pass
        time.sleep(0.1)
    raise TimeoutError("release API health check timed out")


def _stop_server(server: subprocess.Popen[bytes]) -> None:
    if server.poll() is not None:
        return
    server.terminate()
    try:
        server.wait(timeout=10)
    except subprocess.TimeoutExpired:
        server.kill()
        server.wait(timeout=10)


def _start_server(package_root: Path, log: BinaryIO) -> subprocess.Popen[bytes]:
    if not _port_available():
        raise RuntimeError("127.0.0.1:8100 is already occupied")
    return subprocess.Popen(
        _server_command(package_root),
        cwd=package_root / "app",
        env=_runtime_environment(package_root),
        stdout=log,
        stderr=subprocess.STDOUT,
        creationflags=getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0),
    )


def verify_extracted_release(
    archive: Path,
    python312: Path,
    *,
    launch_edge: bool = False,
) -> ReleaseVerification:
    archive = archive.resolve()
    python312 = python312.resolve()
    if launch_edge:
        raise ValueError(
            "Edge acceptance is interactive; run 启动 TraceFold.cmd from the extraction"
        )
    if not archive.is_file():
        raise FileNotFoundError(f"release archive is missing: {archive}")
    if not python312.is_file():
        raise FileNotFoundError(f"Python interpreter is missing: {python312}")
    _validate_external_checksum(archive)

    with tempfile.TemporaryDirectory(prefix="tracefold-release-verify-") as temporary:
        extraction_root = Path(temporary)
        with zipfile.ZipFile(archive) as source:
            _members, top_level = _safe_zip_members(source)
            source.extractall(extraction_root)
        package_root = extraction_root / top_level
        _verify_package_manifest(package_root)

        install_environment = dict(os.environ)
        install_environment.update(
            {
                "PIP_NO_INDEX": "1",
                "PIP_INDEX_URL": "https://invalid.tracefold.test/simple",
                "PIP_DISABLE_PIP_VERSION_CHECK": "1",
            }
        )
        install = subprocess.run(
            [str(python312), str(package_root / "app" / "install_test_env.py")],
            cwd=package_root,
            env=install_environment,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=600,
            check=False,
        )
        install_log = install.stdout
        no_index = "--no-index" in install_log
        network_download = any(
            ("http://" in line or "https://" in line)
            and ("download" in line.casefold() or "collecting" in line.casefold())
            for line in install_log.splitlines()
        )
        if install.returncode != 0:
            raise RuntimeError(
                f"offline release installation failed ({install.returncode}):\n"
                f"{install_log}"
            )
        if not no_index or network_download:
            raise RuntimeError("release installation did not prove offline pip usage")

        sentinel = package_root / "data" / "inbox" / "restart-sentinel.txt"
        database = package_root / "data" / "media.sqlite3"
        server_log_path = package_root / "runtime" / "logs" / "verification.log"
        server_log_path.parent.mkdir(parents=True, exist_ok=True)
        with server_log_path.open("ab") as server_log:
            first = _start_server(package_root, server_log)
            try:
                health_payload = _wait_for_health(first)
                with urllib.request.urlopen(
                    "http://127.0.0.1:8100/",
                    timeout=2,
                ) as frontend:
                    frontend_status = frontend.status
                api_status, _api_payload = _request_json(
                    "http://127.0.0.1:8100/api/settings"
                )
                sentinel.write_text("preserve me", encoding="utf-8")
                if not database.is_file():
                    raise RuntimeError("package database was not created")
            finally:
                _stop_server(first)
            first_release = _wait_for_port_release()

            second = _start_server(package_root, server_log)
            try:
                _wait_for_health(second)
                restart_preserved = (
                    sentinel.read_text(encoding="utf-8") == "preserve me"
                    and database.is_file()
                )
            finally:
                _stop_server(second)
            second_release = _wait_for_port_release()

        return ReleaseVerification(
            install_returncode=install.returncode,
            health_payload=health_payload,
            frontend_status=frontend_status,
            api_status=api_status,
            port_released=first_release and second_release,
            install_log_contains_no_index=no_index,
            restart_preserved_data=restart_preserved,
            network_download_detected=network_download,
        )


def acquire_wheels(layout: BuildLayout, log: BinaryIO) -> None:
    project_version, dependencies = _read_project(layout.repo_root)
    package_version = layout.package_root.name.removeprefix(f"{PACKAGE_PREFIX}-")
    if project_version != TRACEFOLD_TEST_RELEASE_VERSION:
        raise ValueError(
            "TRACEFOLD_TEST_RELEASE_VERSION does not match media API project version"
        )
    if package_version != project_version:
        raise ValueError(
            f"requested version {package_version} does not match project {project_version}"
        )

    download = [
        sys.executable,
        "-m",
        "pip",
        "download",
        "--dest",
        str(layout.wheelhouse),
        "--only-binary=:all:",
        "--platform",
        "win_amd64",
        "--python-version",
        "312",
        "--implementation",
        "cp",
        "--abi",
        "cp312",
        *dependencies,
    ]
    _run_logged(download, cwd=layout.repo_root, log=log)
    wheel = [
        sys.executable,
        "-m",
        "pip",
        "wheel",
        "--no-deps",
        "--wheel-dir",
        str(layout.wheelhouse),
        str(layout.repo_root / "apps" / "media-api"),
    ]
    _run_logged(wheel, cwd=layout.repo_root, log=log)
    write_hashed_lock(layout.wheelhouse, layout.lockfile)


def _iter_release_files(package_root: Path) -> Iterable[Path]:
    return sorted(
        (path for path in package_root.rglob("*") if path.is_file()),
        key=lambda path: path.relative_to(package_root.parent).as_posix(),
    )


def create_archive(layout: BuildLayout) -> None:
    layout.artifact.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(
        layout.artifact,
        "w",
        compression=zipfile.ZIP_DEFLATED,
        compresslevel=9,
    ) as archive:
        for path in _iter_release_files(layout.package_root):
            relative = path.relative_to(layout.package_root.parent).as_posix()
            info = zipfile.ZipInfo(relative, FIXED_ZIP_TIMESTAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes(), compresslevel=9)

    digest = _sha256(layout.artifact)
    layout.checksum.write_text(
        f"{digest}  {layout.artifact.name}\n",
        encoding="utf-8",
        newline="\n",
    )


def build_release(
    repo_root: Path,
    version: str = TRACEFOLD_TEST_RELEASE_VERSION,
) -> Path:
    layout = BuildLayout.for_repo(repo_root, version)
    reset_staging(layout)
    layout.staging_root.mkdir(parents=True)

    with layout.build_log.open("wb") as log:
        _run_logged(
            [sys.executable, "-m", "pytest", "apps/media-api/tests", "-q"],
            cwd=layout.repo_root,
            log=log,
        )
        _run_logged(
            ["npm", "--prefix", "apps/media-web", "test"],
            cwd=layout.repo_root,
            log=log,
        )
        _run_logged(
            ["npm", "--prefix", "apps/media-web", "run", "build"],
            cwd=layout.repo_root,
            log=log,
        )
        assemble(layout)
        acquire_wheels(layout, log)
        manifest = write_manifest(layout.package_root)
        create_archive(layout)

        wheel_count = len(list(layout.wheelhouse.glob("*.whl")))
        manifest_count = len(manifest.read_text(encoding="utf-8").splitlines())
        summary = (
            f"\nartifact={layout.artifact}\n"
            f"size={layout.artifact.stat().st_size}\n"
            f"sha256={_sha256(layout.artifact)}\n"
            f"wheels={wheel_count}\n"
            f"manifest_files={manifest_count}\n"
            f"build_log={layout.build_log}\n"
        )
        log.write(summary.encode("utf-8"))
        print(summary, end="")
    return layout.artifact


def _repo_root_from_script() -> Path:
    return Path(__file__).resolve().parents[1]


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build the TraceFold offline Windows x64 test package."
    )
    parser.add_argument("--version", default=TRACEFOLD_TEST_RELEASE_VERSION)
    parser.add_argument("--verify-only", type=Path)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    if args.verify_only is not None:
        result = verify_extracted_release(
            args.verify_only,
            Path(sys.executable),
        )
        print(json.dumps(result.__dict__, ensure_ascii=False, indent=2))
        return 0
    build_release(_repo_root_from_script(), args.version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
