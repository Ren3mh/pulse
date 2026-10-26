import uuid
from datetime import timezone


def make_event(**overrides):
    event = {
        "version": 1,
        "event_id": str(uuid.uuid4()),
        "event_name": "product_viewed",
        "distinct_id": "anon-test01",
        "occurred_at": "2026-10-26T13:00:00Z",
        "properties": {"product_id": "fjord-lounge-chair"},
    }
    event.update(overrides)
    return event


def test_health(client):
    assert client.get("/health").json() == {"status": "ok"}


def test_valid_event_is_stored(client, fetch_events):
    event = make_event()

    response = client.post("/events", json=event)

    assert response.status_code == 202
    assert response.json() == {"event_id": event["event_id"], "duplicate": False}
    rows = fetch_events()
    assert len(rows) == 1
    assert rows[0][1] == "product_viewed"
    assert rows[0][5] == {"product_id": "fjord-lounge-chair"}


def test_same_event_id_is_stored_once(client, fetch_events):
    event = make_event()

    client.post("/events", json=event)
    second = client.post("/events", json=event)

    assert second.status_code == 202
    assert second.json()["duplicate"] is True
    assert len(fetch_events()) == 1


def test_received_at_is_set_by_server(client, fetch_events):
    client.post("/events", json=make_event(occurred_at="2020-01-01T00:00:00Z"))

    occurred_at, received_at = fetch_events()[0][3:5]
    assert received_at > occurred_at


def test_client_cannot_send_received_at(client):
    response = client.post("/events", json=make_event(received_at="2026-10-26T13:00:00Z"))
    assert response.status_code == 422


def test_occurred_at_is_stored_in_utc(client, fetch_events):
    client.post("/events", json=make_event(occurred_at="2026-10-26T14:00:00+01:00"))

    occurred_at = fetch_events()[0][3]
    assert occurred_at.astimezone(timezone.utc).hour == 13


def test_properties_default_to_empty(client, fetch_events):
    event = make_event()
    del event["properties"]

    assert client.post("/events", json=event).status_code == 202
    assert fetch_events()[0][5] == {}


def test_missing_required_field_is_rejected(client):
    for field in ["version", "event_id", "event_name", "distinct_id", "occurred_at"]:
        event = make_event()
        del event[field]
        assert client.post("/events", json=event).status_code == 422, field


def test_unknown_version_is_rejected(client):
    assert client.post("/events", json=make_event(version=2)).status_code == 422


def test_event_name_must_be_snake_case_object_verb(client):
    for bad in ["ProductViewed", "viewed", "product-viewed", "product viewed"]:
        assert client.post("/events", json=make_event(event_name=bad)).status_code == 422, bad


def test_occurred_at_without_timezone_is_rejected(client):
    response = client.post("/events", json=make_event(occurred_at="2026-10-26T13:00:00"))
    assert response.status_code == 422


def test_properties_must_be_flat(client):
    response = client.post("/events", json=make_event(properties={"cart": {"items": 2}}))
    assert response.status_code == 422


def test_property_keys_must_be_snake_case(client):
    response = client.post("/events", json=make_event(properties={"productId": "x"}))
    assert response.status_code == 422
