from __future__ import annotations

from fastapi.testclient import TestClient

from media_api.intake.models import InboxItem, SourceFile
from media_api.intake.state import InboxStatus
from media_api.storage.local import LocalStorage


def test_domain_error_has_stable_shape(
    client: TestClient,
    storage: LocalStorage,
) -> None:
    source = storage.inbox_dir / "changed.mp4"
    source.write_bytes(b"original")
    item = InboxItem(
        media_type="video",
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=source.name,
    )
    item.sources.append(SourceFile.from_snapshot(storage.snapshot(source), is_primary=True))
    with client.app.state.session_factory() as session:
        session.add(item)
        session.commit()
        item_id = item.id
    source.write_bytes(b"changed-content")

    response = client.post(
        f"/api/inbox/items/{item_id}/ingest",
        headers={"Idempotency-Key": "changed"},
    )

    assert response.status_code == 409
    assert response.json() == {
        "error": {
            "code": "SOURCE_CHANGED",
            "message": "源文件已发生变化，请重新扫描。",
            "details": {"item_id": item_id},
        }
    }
    assert response.headers["x-correlation-id"]


def test_ingest_is_disabled_while_startup_recovery_is_active(
    client: TestClient,
) -> None:
    client.app.state.runtime_state.ingest_enabled = False
    try:
        response = client.post(
            "/api/inbox/items/not-started/ingest",
            headers={"Idempotency-Key": "blocked-during-recovery"},
        )
    finally:
        client.app.state.runtime_state.ingest_enabled = True

    assert response.status_code == 503
    assert response.json() == {
        "error": {
            "code": "SYSTEM_RECOVERING",
            "message": "系统正在恢复未完成操作，请稍后重试。",
            "details": {"item_id": "not-started"},
        }
    }
