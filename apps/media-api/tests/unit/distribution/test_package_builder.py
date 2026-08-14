from __future__ import annotations

import hashlib
import importlib.util
import sys
import zipfile
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[5]
BUILDER_PATH = REPO_ROOT / "scripts" / "build_windows_test_package.py"


def load_builder():
    spec = importlib.util.spec_from_file_location(
        "tracefold_test_package_builder",
        BUILDER_PATH,
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def builder():
    return load_builder()


@pytest.fixture
def fake_repo(tmp_path: Path) -> Path:
    api = tmp_path / "apps" / "media-api"
    web = tmp_path / "apps" / "media-web"
    packaging = tmp_path / "packaging" / "windows-test"

    (api / "src" / "media_api").mkdir(parents=True)
    (api / "src" / "media_api" / "__init__.py").write_text(
        "__version__ = '0.1.0'\n",
        encoding="utf-8",
    )
    (api / "src" / "media_api" / "__pycache__").mkdir()
    (api / "src" / "media_api" / "__pycache__" / "cached.pyc").write_bytes(
        b"cache"
    )
    (api / "migrations" / "versions").mkdir(parents=True)
    (api / "migrations" / "env.py").write_text("# migration\n", encoding="utf-8")
    (api / "migrations" / "versions" / "001.py").write_text(
        "# revision\n",
        encoding="utf-8",
    )
    (api / "migrations" / "versions" / "__pycache__").mkdir()
    (
        api
        / "migrations"
        / "versions"
        / "__pycache__"
        / "001.cpython-312.pyc"
    ).write_bytes(b"cache")
    (api / "alembic.ini").write_text("[alembic]\n", encoding="utf-8")
    (api / "tests").mkdir()
    (api / "tests" / "secret.sqlite3").write_bytes(b"private")

    (web / "dist" / "assets").mkdir(parents=True)
    (web / "dist" / "index.html").write_text("<main>TraceFold</main>", encoding="utf-8")
    (web / "dist" / "assets" / "app.js").write_text("// built\n", encoding="utf-8")
    (web / "node_modules").mkdir()

    packaging.mkdir(parents=True)
    for name in ("common.py", "install_test_env.py", "launch_test_app.py"):
        (packaging / name).write_text(f"# {name}\n", encoding="utf-8")
    for name in ("安装测试环境.cmd", "启动 TraceFold.cmd", "测试版使用说明.txt"):
        (packaging / name).write_text(f"{name}\n", encoding="utf-8")
    (tmp_path / ".env").write_text("SECRET=yes\n", encoding="utf-8")
    return tmp_path


def make_wheel(directory: Path, *, name: str, version: str) -> Path:
    normalized = name.replace("-", "_")
    wheel = directory / f"{normalized}-{version}-py3-none-any.whl"
    metadata = f"Metadata-Version: 2.1\nName: {name}\nVersion: {version}\n"
    with zipfile.ZipFile(wheel, "w") as archive:
        archive.writestr(f"{normalized}-{version}.dist-info/METADATA", metadata)
    return wheel


def test_reset_staging_rejects_any_root_outside_named_build_directory(
    tmp_path: Path,
    builder,
) -> None:
    unsafe = builder.BuildLayout(
        repo_root=tmp_path,
        staging_root=tmp_path / "dist",
        package_root=tmp_path / "dist" / "TraceFold-Test-0.1.0",
        artifact=tmp_path / "artifact.zip",
    )

    with pytest.raises(ValueError, match="build/windows-test-package"):
        builder.reset_staging(unsafe)


def test_reset_staging_preserves_unrelated_build_files(
    tmp_path: Path,
    builder,
) -> None:
    layout = builder.BuildLayout.for_repo(tmp_path, "0.1.0")
    layout.staging_root.mkdir(parents=True)
    (layout.staging_root / "old.txt").write_text("old", encoding="utf-8")
    unrelated = tmp_path / "build" / "keep.txt"
    unrelated.write_text("keep", encoding="utf-8")

    builder.reset_staging(layout)

    assert unrelated.read_text(encoding="utf-8") == "keep"
    assert not layout.staging_root.exists()


def test_assembly_contains_runtime_only(fake_repo: Path, builder) -> None:
    layout = builder.BuildLayout.for_repo(fake_repo, "0.1.0")

    package = builder.assemble(layout)

    assert (package / "app" / "media_api").is_dir()
    assert (package / "app" / "migrations").is_dir()
    assert (package / "app" / "web" / "index.html").is_file()
    assert (package / "app" / "install_test_env.py").is_file()
    assert (package / "app" / "launch_test_app.py").is_file()
    assert (package / "安装测试环境.cmd").is_file()
    assert (package / "启动 TraceFold.cmd").is_file()
    assert (package / "测试版使用说明.txt").is_file()
    assert not (package / "app" / "tests").exists()
    assert not list(package.rglob("*.sqlite3"))
    assert not list(package.rglob("node_modules"))
    assert not list(package.rglob(".env"))
    assert not list(package.rglob("__pycache__"))
    assert not list(package.rglob("*.pyc"))
    assert (package / "data" / "inbox" / ".gitkeep").is_file()
    assert (package / "data" / "library" / ".gitkeep").is_file()
    assert (package / "data" / "work" / ".gitkeep").is_file()
    assert (package / "runtime" / "logs" / ".gitkeep").is_file()
    assert (package / "runtime" / "edge-profile" / ".gitkeep").is_file()


def test_hashed_lock_uses_wheel_metadata_and_sha256(
    tmp_path: Path,
    builder,
) -> None:
    wheel = make_wheel(tmp_path, name="sample-package", version="1.2.3")
    lock = tmp_path / "requirements.lock"

    builder.write_hashed_lock(tmp_path, lock)

    digest = hashlib.sha256(wheel.read_bytes()).hexdigest()
    assert lock.read_text(encoding="utf-8") == (
        f"sample-package==1.2.3 --hash=sha256:{digest}\n"
    )


def test_hashed_lock_rejects_duplicate_normalized_names(
    tmp_path: Path,
    builder,
) -> None:
    make_wheel(tmp_path, name="sample-package", version="1.2.3")
    make_wheel(tmp_path, name="sample_package", version="1.2.4")

    with pytest.raises(ValueError, match="duplicate"):
        builder.write_hashed_lock(tmp_path, tmp_path / "requirements.lock")


def test_hashed_lock_rejects_source_distributions(
    tmp_path: Path,
    builder,
) -> None:
    make_wheel(tmp_path, name="sample-package", version="1.2.3")
    (tmp_path / "unsafe-1.0.0.tar.gz").write_bytes(b"source")

    with pytest.raises(ValueError, match="wheel"):
        builder.write_hashed_lock(tmp_path, tmp_path / "requirements.lock")


def test_manifest_excludes_mutable_data_contents(
    tmp_path: Path,
    builder,
) -> None:
    package = tmp_path / "TraceFold-Test-0.1.0"
    inbox = package / "data" / "inbox"
    inbox.mkdir(parents=True)
    (inbox / ".gitkeep").write_text("", encoding="utf-8")
    (inbox / "private.jpg").write_bytes(b"private")

    with pytest.raises(ValueError, match="mutable data"):
        builder.write_manifest(package)


def test_manifest_is_sorted_and_does_not_hash_itself(
    tmp_path: Path,
    builder,
) -> None:
    package = tmp_path / "TraceFold-Test-0.1.0"
    (package / "app").mkdir(parents=True)
    (package / "app" / "z.txt").write_text("z", encoding="utf-8")
    (package / "app" / "a.txt").write_text("a", encoding="utf-8")

    manifest = builder.write_manifest(package)

    lines = manifest.read_text(encoding="utf-8").splitlines()
    assert lines == sorted(lines, key=lambda line: line.split("  ", 1)[1])
    assert not any("MANIFEST.sha256" in line for line in lines)
