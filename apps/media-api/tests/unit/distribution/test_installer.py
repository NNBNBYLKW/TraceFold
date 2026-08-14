from __future__ import annotations

import importlib.util
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


common = load_module("tracefold_dist_common", PACKAGING_ROOT / "common.py")
sys.modules["common"] = common
installer = load_module(
    "tracefold_dist_installer",
    PACKAGING_ROOT / "install_test_env.py",
)

PackagePaths = common.PackagePaths
PrerequisiteError = common.PrerequisiteError
find_edge = common.find_edge
runtime_environment = common.runtime_environment
validate_python = common.validate_python
offline_install_command = installer.offline_install_command
migration_command = installer.migration_command


@pytest.fixture
def package_paths(tmp_path: Path):
    script = tmp_path / "TraceFold-Test-0.1.0" / "app" / "install_test_env.py"
    return PackagePaths.from_script(script)


def test_package_paths_are_rooted_beside_the_extracted_package(
    package_paths,
) -> None:
    assert package_paths.root.name == "TraceFold-Test-0.1.0"
    assert package_paths.venv == package_paths.root / ".venv"
    assert package_paths.database == package_paths.root / "data" / "media.sqlite3"
    assert package_paths.web == package_paths.root / "app" / "web"


def test_install_command_is_strictly_offline(package_paths) -> None:
    command = offline_install_command(package_paths)

    assert command[:4] == [
        str(package_paths.venv_python),
        "-m",
        "pip",
        "install",
    ]
    assert "--no-index" in command
    assert "--require-hashes" in command
    assert command[command.index("--find-links") + 1] == str(
        package_paths.wheelhouse
    )
    assert command[command.index("-r") + 1] == str(
        package_paths.app / "requirements-win-py312.lock"
    )
    assert not any(
        "http://" in part or "https://" in part for part in command
    )


def test_migration_uses_package_configuration(package_paths) -> None:
    assert migration_command(package_paths) == [
        str(package_paths.venv_python),
        "-m",
        "alembic",
        "-c",
        str(package_paths.app / "alembic.ini"),
        "-x",
        f"database_path={package_paths.database.resolve()}",
        "upgrade",
        "head",
    ]


def test_runtime_environment_uses_only_package_local_paths(package_paths) -> None:
    environment = runtime_environment(package_paths, base={"SYSTEMROOT": "C:/Windows"})

    assert environment["SYSTEMROOT"] == "C:/Windows"
    assert environment["TRACEFOLD_MEDIA_INBOX_DIR"] == str(
        package_paths.data / "inbox"
    )
    assert environment["TRACEFOLD_MEDIA_LIBRARY_DIR"] == str(
        package_paths.data / "library"
    )
    assert environment["TRACEFOLD_MEDIA_WORK_DIR"] == str(
        package_paths.data / "work"
    )
    assert environment["TRACEFOLD_MEDIA_DATABASE_PATH"] == str(
        package_paths.database
    )
    assert environment["TRACEFOLD_MEDIA_WEB_DIST_DIR"] == str(package_paths.web)


@pytest.mark.parametrize(
    ("version", "pointer_bits", "machine"),
    [
        ((3, 11, 9), 64, "AMD64"),
        ((3, 12, 4), 32, "AMD64"),
        ((3, 12, 4), 64, "ARM64"),
    ],
)
def test_rejects_unsupported_python_runtime(
    version: tuple[int, int, int],
    pointer_bits: int,
    machine: str,
) -> None:
    with pytest.raises(PrerequisiteError, match="Python 3.12 x64"):
        validate_python(version, pointer_bits=pointer_bits, machine=machine)


def test_accepts_python_312_x64() -> None:
    validate_python((3, 12, 8), pointer_bits=64, machine="AMD64")


def test_edge_search_uses_known_windows_locations(tmp_path: Path) -> None:
    edge = tmp_path / "Microsoft" / "Edge" / "Application" / "msedge.exe"
    edge.parent.mkdir(parents=True)
    edge.write_bytes(b"")

    result = find_edge({"ProgramFiles": str(tmp_path)}, which=lambda _: None)

    assert result == edge.resolve()
