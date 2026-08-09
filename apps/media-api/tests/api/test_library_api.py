from __future__ import annotations

from dataclasses import dataclass

import pytest
from fastapi.testclient import TestClient

from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage


@dataclass(frozen=True)
class SeededAsset:
    id: str
    actual_filename: str


@pytest.fixture
def asset(client: TestClient, storage: LocalStorage) -> SeededAsset:
    source = storage.inbox_dir / "library-source.mp4"
    source.write_bytes(b"library-video")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
        display_name="Holiday clip",
    )
    item.sources.append(
        SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
    )
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()

    response = client.post(
        f"/api/inbox/items/{item.id}/ingest",
        headers={"Idempotency-Key": "seed-library"},
    )
    assert response.status_code == 200
    return SeededAsset(
        id=response.json()["asset_id"],
        actual_filename=source.name,
    )


def test_library_lists_details_and_searches_display_or_actual_name(
    client: TestClient,
    asset: SeededAsset,
) -> None:
    listing = client.get("/api/library/assets")
    display_search = client.get("/api/library/assets", params={"search": "holiday"})
    actual_search = client.get(
        "/api/library/assets",
        params={"search": "library-source"},
    )
    missing_search = client.get("/api/library/assets", params={"search": "missing"})
    detail = client.get(f"/api/library/assets/{asset.id}")

    assert listing.status_code == 200
    assert [entry["id"] for entry in listing.json()] == [asset.id]
    assert [entry["id"] for entry in display_search.json()] == [asset.id]
    assert [entry["id"] for entry in actual_search.json()] == [asset.id]
    assert missing_search.json() == []
    assert detail.status_code == 200
    assert detail.json()["actual_filename"] == asset.actual_filename


def test_metadata_patch_cannot_rename_physical_file(
    client: TestClient,
    asset: SeededAsset,
    storage: LocalStorage,
) -> None:
    response = client.patch(
        f"/api/library/assets/{asset.id}/metadata",
        json={
            "display_name": "New title",
            "author_ids": [],
            "series_id": None,
            "tag_ids": [],
            "actual_filename": "renamed.mp4",
        },
    )

    assert response.status_code == 422
    assert (storage.library_dir / asset.actual_filename).exists()
    assert not (storage.library_dir / "renamed.mp4").exists()


def test_library_metadata_patch_changes_display_and_taxonomy_only(
    client: TestClient,
    asset: SeededAsset,
    storage: LocalStorage,
) -> None:
    author_id = client.post("/api/authors", json={"name": "Alice"}).json()["id"]
    series_id = client.post("/api/series", json={"name": "Trips"}).json()["id"]
    tag_id = client.post("/api/tags", json={"name": "travel"}).json()["id"]

    response = client.patch(
        f"/api/library/assets/{asset.id}/metadata",
        json={
            "display_name": "New title",
            "author_ids": [author_id],
            "series_id": series_id,
            "tag_ids": [tag_id],
        },
    )

    assert response.status_code == 200
    assert response.json()["display_name"] == "New title"
    assert response.json()["author_ids"] == [author_id]
    assert response.json()["series_id"] == series_id
    assert response.json()["tag_ids"] == [tag_id]
    assert response.json()["actual_filename"] == asset.actual_filename
    assert (storage.library_dir / asset.actual_filename).exists()
