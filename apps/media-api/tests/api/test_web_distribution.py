from __future__ import annotations

from collections.abc import Generator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base
from media_api.main import create_app


@pytest.fixture
def web_dist(tmp_path: Path) -> Path:
    root = tmp_path / "web-dist"
    assets = root / "assets"
    assets.mkdir(parents=True)
    (root / "index.html").write_text(
        "<main>TraceFold Test</main>",
        encoding="utf-8",
    )
    (assets / "app.js").write_text(
        "window.TRACEFOLD = true;",
        encoding="utf-8",
    )
    return root


@pytest.fixture
def client_with_web(
    settings: BootstrapSettings,
    web_dist: Path,
) -> Generator[TestClient, None, None]:
    application = create_app(settings.model_copy(update={"web_dist_dir": web_dist}))
    engine = application.state.session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    with TestClient(application, base_url="http://127.0.0.1") as test_client:
        yield test_client
    Base.metadata.drop_all(engine)
    engine.dispose()


def test_serves_frontend_and_spa_routes(client_with_web: TestClient) -> None:
    assert client_with_web.get("/").text == "<main>TraceFold Test</main>"
    assert client_with_web.get("/library").text == "<main>TraceFold Test</main>"
    assert client_with_web.get("/assets/app.js").text == "window.TRACEFOLD = true;"


def test_api_and_health_routes_win_over_spa(client_with_web: TestClient) -> None:
    assert client_with_web.get("/health").json()["status"] == "ok"

    response = client_with_web.get("/api/not-a-real-route")

    assert response.status_code == 404
    assert "TraceFold Test" not in response.text


def test_non_get_request_is_never_spa_fallback(
    client_with_web: TestClient,
) -> None:
    response = client_with_web.post("/library")

    assert response.status_code == 405
    assert "TraceFold Test" not in response.text


def test_invalid_web_distribution_fails_at_startup(
    settings: BootstrapSettings,
) -> None:
    missing = settings.model_copy(
        update={"web_dist_dir": settings.work_dir / "missing-web"}
    )

    with pytest.raises(ValueError, match="index.html"):
        create_app(missing)
