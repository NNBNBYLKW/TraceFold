from fastapi.testclient import TestClient

from media_api.intake.models import InboxItem
from media_api.intake.state import InboxStatus
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
    assert item["preview_url"] == f"/api/inbox/items/{item['id']}/preview"
    assert item["progress_percent"] is None
    assert item["error"] is None
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


def test_failed_item_exposes_safe_card_error(client: TestClient) -> None:
    item = InboxItem(
        media_type="video",
        status=InboxStatus.FAILED,
        suggested_actual_filename="broken.mp4",
        error_code="PROCESSOR_INPUT_INVALID",
        error_message="文件无法完成前置处理。",
    )
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()
        item_id = item.id

    response = client.get(f"/api/inbox/items/{item_id}")

    assert response.status_code == 200
    assert response.json()["error"] == {
        "code": "PROCESSOR_INPUT_INVALID",
        "message": "文件无法完成前置处理。",
    }
