from __future__ import annotations

import hashlib
import importlib.util
import os
import shutil
import subprocess
import sys
import textwrap
import zipfile
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[5]
BUILDER_PATH = REPO_ROOT / "scripts" / "build_windows_test_package.py"


def load_builder():
    spec = importlib.util.spec_from_file_location(
        "tracefold_release_verifier",
        BUILDER_PATH,
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_manifest(package: Path) -> None:
    files = sorted(
        (path for path in package.rglob("*") if path.is_file()),
        key=lambda path: path.relative_to(package).as_posix(),
    )
    lines = [
        f"{sha256(path)}  {path.relative_to(package).as_posix()}\n"
        for path in files
    ]
    (package / "MANIFEST.sha256").write_text(
        "".join(lines),
        encoding="utf-8",
    )


@pytest.fixture
def release_fixture(tmp_path: Path) -> Path:
    package = tmp_path / "stage" / "TraceFold-Test-0.1.0"
    app = package / "app"
    module = app / "media_api"
    web = app / "web"
    module.mkdir(parents=True)
    web.mkdir(parents=True)
    (module / "__init__.py").write_text("", encoding="utf-8")
    (module / "main.py").write_text(
        textwrap.dedent(
            """
            from fastapi import FastAPI
            from fastapi.responses import FileResponse
            from pathlib import Path

            def create_app():
                app = FastAPI()

                @app.get("/health")
                def health():
                    return {"service": "tracefold-media-api", "status": "ok"}

                @app.get("/api/settings")
                def settings():
                    return {"mode": "test"}

                @app.get("/")
                def index():
                    return FileResponse(Path(__file__).parents[1] / "web" / "index.html")

                return app
            """
        ).strip()
        + "\n",
        encoding="utf-8",
    )
    (web / "index.html").write_text("<main>TraceFold</main>", encoding="utf-8")
    (app / "install_test_env.py").write_text(
        textwrap.dedent(
            """
            from pathlib import Path
            import sys
            import venv

            root = Path(__file__).resolve().parents[1]
            venv.EnvBuilder(with_pip=True, system_site_packages=True).create(root / ".venv")
            (root / "data" / "inbox").mkdir(parents=True, exist_ok=True)
            (root / "data" / "media.sqlite3").touch()
            print("pip install --no-index --find-links wheelhouse")
            """
        ).strip()
        + "\n",
        encoding="utf-8",
    )
    for relative in ("data/library", "data/work", "runtime/logs"):
        (package / relative).mkdir(parents=True)
    write_manifest(package)

    archive = tmp_path / "TraceFold-Test-0.1.0-win-x64.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
        for path in package.rglob("*"):
            if path.is_file():
                output.write(path, path.relative_to(package.parent).as_posix())
    archive.with_suffix(".zip.sha256").write_text(
        f"{sha256(archive)}  {archive.name}\n",
        encoding="utf-8",
    )
    return archive


def test_release_smoke_uses_no_index_and_preserves_data(
    release_fixture: Path,
) -> None:
    builder = load_builder()

    result = builder.verify_extracted_release(
        release_fixture,
        Path(sys.executable),
        launch_edge=False,
    )

    assert result.install_returncode == 0
    assert result.health_payload == {
        "service": "tracefold-media-api",
        "status": "ok",
    }
    assert result.frontend_status == 200
    assert result.api_status == 200
    assert result.port_released is True
    assert result.install_log_contains_no_index is True
    assert result.restart_preserved_data is True
    assert result.network_download_detected is False


def test_release_verifier_rejects_path_traversal(tmp_path: Path) -> None:
    builder = load_builder()
    archive = tmp_path / "TraceFold-Test-0.1.0-win-x64.zip"
    with zipfile.ZipFile(archive, "w") as output:
        output.writestr("../outside.txt", "unsafe")
    archive.with_suffix(".zip.sha256").write_text(
        f"{sha256(archive)}  {archive.name}\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="unsafe ZIP member"):
        builder.verify_extracted_release(archive, Path(sys.executable))


def test_installed_package_imports_on_python_312() -> None:
    py_launcher = shutil.which("py")
    if py_launcher is None:
        pytest.skip("Windows Python launcher is unavailable")
    environment = dict(os.environ)
    environment["PYTHONPATH"] = str(REPO_ROOT / "apps" / "media-api" / "src")

    completed = subprocess.run(
        [
            py_launcher,
            "-3.12",
            "-c",
            "from media_api.taxonomy.schemas import TaxonomyRead",
        ],
        env=environment,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )

    assert completed.returncode == 0, completed.stdout
