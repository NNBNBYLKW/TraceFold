from __future__ import annotations

import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tomllib
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
    shutil.copytree(source, destination)


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
    rendered = subprocess.list2cmdline([str(part) for part in command])
    header = f"\n> {rendered}\n".encode("utf-8")
    log.write(header)
    log.flush()
    print(rendered)
    process = subprocess.Popen(
        list(command),
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
        raise SystemExit("--verify-only will be implemented by the release verifier")
    build_release(_repo_root_from_script(), args.version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
