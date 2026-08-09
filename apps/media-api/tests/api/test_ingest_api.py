import pytest
from fastapi.testclient import TestClient

from media_api.catalog.models import MediaAsset
from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage


@pytest.fixture
def ready_item(client: TestClient, storage: LocalStorage) -> InboxItem:
    source = storage.inbox_dir / "idempotent.mp4"
    source.write_bytes(b"idempotent-video")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
    )
    item.sources.append(
        SourceFile.from_snapshot(storage.snapshot(source), is_primary=True)
    )
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()
    return item


def test_repeated_ingest_key_returns_same_asset(
    client: TestClient,
    ready_item: InboxItem,
) -> None:
    headers = {"Idempotency-Key": "same-command"}

    first = client.post(
        f"/api/inbox/items/{ready_item.id}/ingest",
        headers=headers,
    )
    second = client.post(
        f"/api/inbox/items/{ready_item.id}/ingest",
        headers=headers,
    )

    assert first.status_code == 200
    assert second.status_code == 200
    assert first.json()["kind"] == "completed"
    assert second.json()["asset_id"] == first.json()["asset_id"]
    with client.app.state.session_factory() as session:
        assert session.query(MediaAsset).count() == 1


def test_ingest_requires_idempotency_key(
    client: TestClient,
    ready_item: InboxItem,
) -> None:
    response = client.post(f"/api/inbox/items/{ready_item.id}/ingest")

    assert response.status_code == 422
