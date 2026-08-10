from __future__ import annotations

from pathlib import Path

from fastapi.testclient import TestClient
from PIL import Image

from media_api.catalog.models import MediaAsset
from media_api.catalog.service import PlatformRevealPort
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage


def _seed_inbox_image(
    client: TestClient,
    storage: LocalStorage,
    fixture: Path,
) -> InboxItem:
    source = storage.inbox_dir / fixture.name
    source.write_bytes(fixture.read_bytes())
    item = InboxItem(
        media_type="image",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()
        session.expunge(item)
    return item


def test_inbox_preview_serves_registered_image_without_changing_status(
    client: TestClient,
    storage: LocalStorage,
    copy_media_fixture,
    tmp_path: Path,
) -> None:
    fixture = copy_media_fixture("photo.jpg", tmp_path / "fixture")
    item = _seed_inbox_image(client, storage, fixture)

    response = client.get(f"/api/inbox/items/{item.id}/preview")

    assert response.status_code == 200
    assert response.headers["content-type"] == "image/jpeg"
    cached = storage.previews_dir / f"{storage.sha256(storage.inbox_dir / fixture.name)}.jpg"
    assert cached.exists()
    with Image.open(cached) as image:
        assert image.size == (64, 64)
    with client.app.state.session_factory() as session:
        assert session.get(InboxItem, item.id).status is InboxStatus.READY_TO_TAG


def test_library_preview_serves_only_registered_asset(
    client: TestClient,
    storage: LocalStorage,
    copy_media_fixture,
    tmp_path: Path,
) -> None:
    fixture = copy_media_fixture("photo.jpg", tmp_path / "fixture")
    formal = storage.library_dir / "formal-photo.jpg"
    formal.write_bytes(fixture.read_bytes())
    asset = MediaAsset(
        media_type="image",
        actual_filename=formal.name,
        final_relative_path=formal.name,
        size=formal.stat().st_size,
        sha256=storage.sha256(formal),
    )
    with client.app.state.session_factory() as session:
        session.add(asset)
        session.commit()
        asset_id = asset.id

    response = client.get(f"/api/library/assets/{asset_id}/preview")
    unknown = client.get("/api/library/assets/missing/preview")

    assert response.status_code == 200
    assert response.headers["content-type"] == "image/jpeg"
    assert unknown.status_code == 404


def test_preview_failure_returns_typed_placeholder_without_changing_item(
    client: TestClient,
    storage: LocalStorage,
) -> None:
    source = storage.inbox_dir / "broken.jpg"
    source.write_bytes(b"not-an-image")
    item = InboxItem(
        media_type="image",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()
        item_id = item.id

    response = client.get(f"/api/inbox/items/{item_id}/preview")

    assert response.status_code == 422
    assert response.json()["error"]["details"]["kind"] == "placeholder"
    assert response.json()["error"]["code"] == "PREVIEW_GENERATION_FAILED"
    with client.app.state.session_factory() as session:
        assert session.get(InboxItem, item_id).status is InboxStatus.READY_TO_TAG


def test_reveal_endpoint_uses_registered_asset_path(
    client: TestClient,
    storage: LocalStorage,
    monkeypatch,
) -> None:
    formal = storage.library_dir / "reveal.mp4"
    formal.write_bytes(b"video")
    asset = MediaAsset(
        media_type="video",
        actual_filename=formal.name,
        final_relative_path=formal.name,
        size=formal.stat().st_size,
        sha256=storage.sha256(formal),
    )
    with client.app.state.session_factory() as session:
        session.add(asset)
        session.commit()
        asset_id = asset.id
    revealed: list[Path] = []
    monkeypatch.setattr(
        PlatformRevealPort,
        "reveal",
        lambda _self, path: revealed.append(path),
    )

    response = client.post(f"/api/library/assets/{asset_id}/reveal")

    assert response.status_code == 200
    assert response.json() == {"asset_id": asset_id, "revealed": True}
    assert revealed == [formal.resolve()]


def test_library_preview_rejects_registered_path_outside_library(
    client: TestClient,
    storage: LocalStorage,
    copy_media_fixture,
    tmp_path: Path,
) -> None:
    fixture = copy_media_fixture("photo.jpg", tmp_path / "fixture")
    outside = storage.work_dir / "outside.jpg"
    outside.write_bytes(fixture.read_bytes())
    asset = MediaAsset(
        media_type="image",
        actual_filename=outside.name,
        final_relative_path="../work/outside.jpg",
        size=outside.stat().st_size,
        sha256=storage.sha256(outside),
    )
    with client.app.state.session_factory() as session:
        session.add(asset)
        session.commit()
        asset_id = asset.id
        digest = asset.sha256

    response = client.get(f"/api/library/assets/{asset_id}/preview")

    assert response.status_code == 404
    assert not (storage.previews_dir / f"{digest}.jpg").exists()
