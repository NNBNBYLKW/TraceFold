from fastapi.testclient import TestClient

from media_api.storage.local import LocalStorage


def test_manual_scan_exposes_one_safe_ready_item_after_two_observations(
    client: TestClient,
    storage: LocalStorage,
) -> None:
    (storage.inbox_dir / "photo.jpg").write_bytes(b"image")

    first = client.post("/api/inbox/scan")
    second = client.post("/api/inbox/scan")
    listing = client.get("/api/inbox/items")

    assert first.status_code == 200
    assert first.json()["created_items"] == 0
    assert second.status_code == 200
    assert second.json()["created_items"] == 1
    assert listing.status_code == 200
    items = listing.json()
    assert len(items) == 1
    item = items[0]
    assert item["status"] == "ready_to_tag"
    assert item["allowed_actions"] == ["edit_metadata", "ingest"]
    assert item["actual_filename"] == "photo.jpg"
    assert item["display_name"] is None
    assert item["author_ids"] == []
    assert item["series_id"] is None
    assert item["tag_ids"] == []
    assert len(item["sources"]) == 1
    source = item["sources"][0]
    assert set(source) == {"id", "filename", "extension", "size", "is_primary"}
    assert source["filename"] == "photo.jpg"
    assert source["extension"] == ".jpg"
    assert source["size"] == 5
    assert source["is_primary"] is True

    detail = client.get(f"/api/inbox/items/{item['id']}")
    assert detail.status_code == 200
    assert detail.json()["id"] == item["id"]


def test_missing_inbox_item_returns_not_found(client: TestClient) -> None:
    response = client.get("/api/inbox/items/missing")

    assert response.status_code == 404
