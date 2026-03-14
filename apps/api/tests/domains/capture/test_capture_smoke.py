from fastapi.testclient import TestClient
import pytest


@pytest.mark.skip(reason="capture endpoints are not implemented yet")
def test_capture_create_list_get_smoke(client: TestClient) -> None:
    """
    Smoke test target:
    1. POST /api/capture
    2. GET /api/capture
    3. GET /api/capture/{id}

    Enable this test after capture endpoints are implemented.
    """
    pass