from __future__ import annotations

from dataclasses import dataclass

import pytest
from fastapi.testclient import TestClient

from media_api.catalog.models import MediaAsset
from media_api.taxonomy.models import Author, Series, Tag


@dataclass(frozen=True)
class SeededAssets:
    matching_id: str
    author_id: str
    series_id: str
    tag_id: str


@pytest.fixture
def seeded_assets(client: TestClient) -> SeededAssets:
    author = Author(normalized_name="alice", display_name="Alice")
    other_author = Author(normalized_name="bob", display_name="Bob")
    series = Series(normalized_name="city", display_name="City")
    tag = Tag(normalized_name="night", display_name="Night")
    other_tag = Tag(normalized_name="day", display_name="Day")
    matching = MediaAsset(
        media_type="video",
        actual_filename="hangzhou-night.mp4",
        final_relative_path="hangzhou-night.mp4",
        display_name="杭州夜游",
        size=10,
        sha256="a" * 64,
        authors=[author],
        series=series,
        tags=[tag],
    )
    wrong_tag = MediaAsset(
        media_type="video",
        actual_filename="hangzhou-day.mp4",
        final_relative_path="hangzhou-day.mp4",
        display_name="杭州白天",
        size=11,
        sha256="b" * 64,
        authors=[author],
        series=series,
        tags=[other_tag],
    )
    wrong_author = MediaAsset(
        media_type="video",
        actual_filename="hangzhou-river.mp4",
        final_relative_path="hangzhou-river.mp4",
        display_name="杭州江景",
        size=12,
        sha256="c" * 64,
        authors=[other_author],
        series=series,
        tags=[tag],
    )
    wrong_type = MediaAsset(
        media_type="image",
        actual_filename="hangzhou-night.jpg",
        final_relative_path="hangzhou-night.jpg",
        display_name="杭州夜游照片",
        size=13,
        sha256="d" * 64,
        authors=[author],
        series=series,
        tags=[tag],
    )
    with client.app.state.session_factory() as session:
        session.add_all([matching, wrong_tag, wrong_author, wrong_type])
        session.commit()
        result = SeededAssets(
            matching_id=matching.id,
            author_id=author.id,
            series_id=series.id,
            tag_id=tag.id,
        )
    return result


def test_combines_name_taxonomy_and_media_filters(
    client: TestClient,
    seeded_assets: SeededAssets,
) -> None:
    response = client.get(
        "/api/library/assets",
        params={
            "search": "杭州",
            "author_id": seeded_assets.author_id,
            "series_id": seeded_assets.series_id,
            "tag_id": seeded_assets.tag_id,
            "media_type": "video",
        },
    )

    assert response.status_code == 200
    assert [item["id"] for item in response.json()] == [seeded_assets.matching_id]


def test_rejects_unknown_media_type_filter(client: TestClient) -> None:
    response = client.get("/api/library/assets", params={"media_type": "audio"})

    assert response.status_code == 422
