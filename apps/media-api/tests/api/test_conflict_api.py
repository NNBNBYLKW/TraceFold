from __future__ import annotations

from pathlib import Path

from fastapi.testclient import TestClient

from media_api.catalog.ingest import IngestService
from media_api.catalog.models import MediaAsset
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus


def _create_conflict(client: TestClient) -> tuple[str, str, str]:
    storage = client.app.state.storage
    final = storage.library_dir / "api-conflict.mp4"
    final.write_bytes(b"existing-api-video")
    source = storage.inbox_dir / final.name
    source.write_bytes(b"incoming-api-video")
    with client.app.state.session_factory() as session:
        asset = MediaAsset(
            media_type="video",
            actual_filename=final.name,
            final_relative_path=final.name,
            display_name="Existing API asset",
            size=final.stat().st_size,
            sha256=storage.sha256(final),
        )
        item = InboxItem(
            media_type="video",
            status=InboxStatus.READY_TO_TAG,
            suggested_actual_filename=source.name,
            display_name="Incoming API item",
        )
        item.sources.append(
            SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
        )
        session.add_all([asset, item])
        session.commit()
        result = IngestService(session=session, storage=storage).ingest(
            item.id, "api-create-conflict"
        )
        return result.operation_id, item.id, asset.id


def test_conflict_detail_has_side_by_side_registered_metadata(
    client: TestClient,
) -> None:
    operation_id, item_id, asset_id = _create_conflict(client)

    response = client.get(f"/api/conflicts/{operation_id}")

    assert response.status_code == 200
    payload = response.json()
    assert payload["operation_id"] == operation_id
    assert payload["conflict_kinds"] == ["name"]
    assert payload["incoming_item"]["id"] == item_id
    assert payload["incoming_item"]["display_name"] == "Incoming API item"
    assert payload["existing_asset"]["id"] == asset_id
    assert payload["existing_asset"]["display_name"] == "Existing API asset"
    assert payload["incoming_preview_url"] == f"/api/inbox/items/{item_id}/preview"
    assert payload["existing_preview_url"] == f"/api/library/assets/{asset_id}/preview"
    assert len(payload["source_hash"]) == 64
    assert payload["requested_actual_filename"] == "api-conflict.mp4"
    assert "canonical_path" not in response.text


def test_conflict_resolution_is_idempotent_and_rejects_changed_choice(
    client: TestClient,
) -> None:
    operation_id, _, _ = _create_conflict(client)
    headers = {"Idempotency-Key": "resolve-api-once"}

    first = client.post(
        f"/api/conflicts/{operation_id}/resolve",
        headers=headers,
        json={"resolution": "keep_both"},
    )
    duplicate = client.post(
        f"/api/conflicts/{operation_id}/resolve",
        headers=headers,
        json={"resolution": "keep_both"},
    )
    changed = client.post(
        f"/api/conflicts/{operation_id}/resolve",
        headers=headers,
        json={"resolution": "replace"},
    )

    assert first.status_code == 200
    assert first.json()["resolution"] == "keep_both"
    assert duplicate.status_code == 200
    assert duplicate.json() == first.json()
    assert changed.status_code == 409


def test_conflict_resolution_requires_idempotency_key(client: TestClient) -> None:
    operation_id, _, _ = _create_conflict(client)

    response = client.post(
        f"/api/conflicts/{operation_id}/resolve",
        json={"resolution": "skip"},
    )

    assert response.status_code == 422
