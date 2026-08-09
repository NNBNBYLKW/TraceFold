import pytest
from sqlalchemy.orm import Session

from media_api.infrastructure.config import BootstrapSettings
from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.taxonomy.service import (
    AuthorService,
    EmptyTaxonomyName,
    normalize_taxonomy_name,
)


@pytest.fixture
def session(settings: BootstrapSettings) -> Session:
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)
    db = session_factory()
    try:
        yield db
    finally:
        db.close()
        Base.metadata.drop_all(engine)
        engine.dispose()


@pytest.fixture
def author_service(session: Session) -> AuthorService:
    return AuthorService(session)


@pytest.mark.parametrize(
    ("raw", "display", "normalized"),
    [
        ("  Alice  ", "Alice", "alice"),
        ("旅行　记录", "旅行 记录", "旅行 记录"),
    ],
)
def test_normalize_taxonomy_name(
    raw: str,
    display: str,
    normalized: str,
) -> None:
    value = normalize_taxonomy_name(raw)

    assert value.display == display
    assert value.normalized == normalized


def test_rejects_empty_taxonomy_name() -> None:
    with pytest.raises(EmptyTaxonomyName):
        normalize_taxonomy_name("　 \t ")


def test_duplicate_author_returns_existing(author_service: AuthorService) -> None:
    first = author_service.create("Alice")
    second = author_service.create(" alice ")

    assert second.id == first.id
    assert second.display_name == "Alice"
