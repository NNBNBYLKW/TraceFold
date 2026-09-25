import shutil
from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.main import create_app
from media_api.storage.contracts import RuntimePaths
from media_api.storage.local import LocalStorage

# Register every model before creating isolated test schemas.
import media_api.catalog.models  # noqa: F401, E402
import media_api.intake.models  # noqa: F401, E402
import media_api.processing.models  # noqa: F401, E402
import media_api.settings.models  # noqa: F401, E402
import media_api.taxonomy.models  # noqa: F401, E402


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
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    application = create_app(settings)
    with TestClient(application, base_url="http://127.0.0.1") as test_client:
        yield test_client
    Base.metadata.drop_all(engine)
    engine.dispose()
    application_session_factory = getattr(
        application.state,
        "session_factory",
        None,
    )
    if application_session_factory is not None:
        application_session_factory.kw["bind"].dispose()


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


@pytest.fixture
def copy_media_fixture():
    fixture_dir = Path(__file__).parent / "fixtures" / "media"

    def copy_fixture(name: str, destination: Path) -> Path:
        destination.mkdir(parents=True, exist_ok=True)
        target = (destination / name).resolve()
        shutil.copy2(fixture_dir / name, target)
        return target

    return copy_fixture
