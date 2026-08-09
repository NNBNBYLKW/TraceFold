from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from media_api.infrastructure.config import BootstrapSettings
from media_api.main import create_app


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
