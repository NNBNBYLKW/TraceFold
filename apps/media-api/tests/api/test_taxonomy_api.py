import pytest
from fastapi.testclient import TestClient


@pytest.mark.parametrize("resource", ["authors", "series", "tags"])
def test_taxonomy_create_list_and_update_without_delete(
    client: TestClient,
    resource: str,
) -> None:
    created = client.post(f"/api/{resource}", json={"name": "  Travel  Notes "})
    duplicate = client.post(f"/api/{resource}", json={"name": "travel notes"})

    assert created.status_code == 200
    assert created.json()["display_name"] == "Travel Notes"
    assert duplicate.status_code == 200
    assert duplicate.json()["id"] == created.json()["id"]

    listing = client.get(f"/api/{resource}")
    assert listing.status_code == 200
    assert [entry["id"] for entry in listing.json()] == [created.json()["id"]]

    updated = client.patch(
        f"/api/{resource}/{created.json()['id']}",
        json={"name": "Journey Notes"},
    )
    assert updated.status_code == 200
    assert updated.json()["display_name"] == "Journey Notes"

    assert client.delete(f"/api/{resource}/{created.json()['id']}").status_code == 405


def test_taxonomy_update_rejects_normalized_name_collision(
    client: TestClient,
) -> None:
    first_response = client.post("/api/authors", json={"name": "Alice"})
    second_response = client.post("/api/authors", json={"name": "Bob"})

    assert first_response.status_code == 200
    assert second_response.status_code == 200
    first = first_response.json()
    second = second_response.json()

    response = client.patch(
        f"/api/authors/{second['id']}",
        json={"name": " alice "},
    )

    assert first["id"] != second["id"]
    assert response.status_code == 409


def test_taxonomy_rejects_blank_name(client: TestClient) -> None:
    response = client.post("/api/tags", json={"name": "　 \t "})

    assert response.status_code == 422
