from __future__ import annotations

from dataclasses import dataclass

import pytest
from fastapi.testclient import TestClient

from media_api.intake.models import InboxItem
from media_api.intake.state import InboxStatus


@dataclass(frozen=True)
class TaxonomyFixture:
    author_ids: list[str]
    series_id: str
    tag_ids: list[str]


@pytest.fixture
def taxonomy(client: TestClient) -> TaxonomyFixture:
    author_ids = [
        client.post("/api/authors", json={"name": name}).json()["id"]
        for name in ("Alice", "Bob", "Carol")
    ]
    series_id = client.post(
        "/api/series",
        json={"name": "City Walk"},
    ).json()["id"]
    tag_ids = [
        client.post("/api/tags", json={"name": name}).json()["id"]
        for name in ("night", "travel", "street")
    ]
    return TaxonomyFixture(
        author_ids=author_ids,
        series_id=series_id,
        tag_ids=tag_ids,
    )


def _create_ready_items(client: TestClient, count: int) -> list[InboxItem]:
    session_factory = client.app.state.session_factory
    with session_factory() as session:
        items = [
            InboxItem(
                media_type="image",
                status=InboxStatus.READY_TO_TAG,
                suggested_actual_filename=f"photo-{index}.jpg",
            )
            for index in range(count)
        ]
        session.add_all(items)
        session.commit()
        return items


@pytest.fixture
def ready_item(client: TestClient) -> InboxItem:
    return _create_ready_items(client, 1)[0]


@pytest.fixture
def two_ready_items(client: TestClient) -> list[InboxItem]:
    return _create_ready_items(client, 2)


def test_updates_multi_author_single_series_and_multi_tag(
    client: TestClient,
    ready_item: InboxItem,
    taxonomy: TaxonomyFixture,
) -> None:
    response = client.patch(
        f"/api/inbox/items/{ready_item.id}/metadata",
        json={
            "display_name": "杭州夜游",
            "author_ids": taxonomy.author_ids[:2],
            "series_id": taxonomy.series_id,
            "tag_ids": taxonomy.tag_ids[:2],
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["display_name"] == "杭州夜游"
    assert body["author_ids"] == taxonomy.author_ids[:2]
    assert body["series_id"] == taxonomy.series_id
    assert body["tag_ids"] == taxonomy.tag_ids[:2]
    assert body["actual_filename"] == "photo-0.jpg"


def test_bulk_metadata_appends_without_erasing_existing(
    client: TestClient,
    two_ready_items: list[InboxItem],
    taxonomy: TaxonomyFixture,
) -> None:
    first_item = two_ready_items[0]
    seed = client.patch(
        f"/api/inbox/items/{first_item.id}/metadata",
        json={
            "display_name": None,
            "author_ids": [taxonomy.author_ids[1]],
            "series_id": None,
            "tag_ids": [taxonomy.tag_ids[1]],
        },
    )
    assert seed.status_code == 200

    response = client.patch(
        "/api/inbox/items/bulk-metadata",
        json={
            "item_ids": [item.id for item in two_ready_items],
            "append_author_ids": [taxonomy.author_ids[0]],
            "append_tag_ids": [taxonomy.tag_ids[0]],
            "replace_series": True,
            "replace_series_id": taxonomy.series_id,
        },
    )

    assert response.status_code == 200
    assert response.json()["updated_count"] == 2
    first = client.get(f"/api/inbox/items/{first_item.id}").json()
    second = client.get(f"/api/inbox/items/{two_ready_items[1].id}").json()
    assert set(first["author_ids"]) == {
        taxonomy.author_ids[0],
        taxonomy.author_ids[1],
    }
    assert set(first["tag_ids"]) == {
        taxonomy.tag_ids[0],
        taxonomy.tag_ids[1],
    }
    assert second["author_ids"] == [taxonomy.author_ids[0]]
    assert second["tag_ids"] == [taxonomy.tag_ids[0]]
    assert first["series_id"] == taxonomy.series_id
    assert second["series_id"] == taxonomy.series_id


def test_bulk_metadata_validates_every_reference_before_updating_any_item(
    client: TestClient,
    two_ready_items: list[InboxItem],
    taxonomy: TaxonomyFixture,
) -> None:
    response = client.patch(
        "/api/inbox/items/bulk-metadata",
        json={
            "item_ids": [item.id for item in two_ready_items],
            "append_author_ids": [taxonomy.author_ids[0]],
            "append_tag_ids": ["missing-tag"],
            "replace_series": False,
            "replace_series_id": None,
        },
    )

    assert response.status_code == 404
    for item in two_ready_items:
        unchanged = client.get(f"/api/inbox/items/{item.id}").json()
        assert unchanged["author_ids"] == []
        assert unchanged["tag_ids"] == []
        assert unchanged["series_id"] is None
