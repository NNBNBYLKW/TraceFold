from collections.abc import Generator

import pytest
from fastapi.testclient import TestClient

from app.main import app


@pytest.fixture
def client() -> Generator[TestClient, None, None]:
    with TestClient(app) as test_client:
        yield test_client


@pytest.fixture(autouse=True)
def stub_ai_provider(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        "app.ai.service.generate_knowledge_summary_content",
        lambda knowledge_entry: {
            "summary": "Stub knowledge summary for tests.",
            "key_points": [
                "Stub key point one.",
                "Stub key point two.",
            ],
            "keywords": ["stub", "knowledge", "summary"],
        },
    )
