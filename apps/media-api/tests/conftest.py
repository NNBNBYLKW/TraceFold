from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from media_api.infrastructure.config import BootstrapSettings
from media_api.main import create_app
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage


@pytest.fixture
def settings(tmp_path: Path) -> BootstrapSettings:
    return BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
    )


@pytest.fixture
def client(settings: BootstrapSettings) -> Generator[TestClient, None, None]:
    with TestClient(create_app(settings)) as test_client:
        yield test_client


@pytest.fixture
def runtime_paths(settings: BootstrapSettings) -> RuntimePaths:
    return RuntimePaths(
        inbox_dir=settings.inbox_dir,
        library_dir=settings.library_dir,
        work_dir=settings.work_dir,
    )


@pytest.fixture
def storage(
    settings: BootstrapSettings,
    runtime_paths: RuntimePaths,
) -> LocalStorage:
    gateway = LocalStorage(settings)
    gateway.provision_directories(runtime_paths)
    return gateway


@pytest.fixture
def inbox_file(storage: LocalStorage) -> Path:
    source = storage.inbox_dir / "clip.mp4"
    source.write_bytes(b"media-bytes")
    return source
