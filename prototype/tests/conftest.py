"""Tests run against a real Postgres database (pulse_test), not mocks.

Set TEST_DATABASE_URL to point elsewhere.
"""

import os

import pytest

os.environ["DATABASE_URL"] = os.environ.get(
    "TEST_DATABASE_URL", "postgresql://pulse:pulse@localhost:5432/pulse_test"
)

from fastapi.testclient import TestClient  # noqa: E402

from pulse import db  # noqa: E402
from pulse.app import app  # noqa: E402


@pytest.fixture
def client():
    # Entering the client runs startup: schema + seed.
    with TestClient(app) as test_client:
        with db.connect() as conn:
            conn.execute("TRUNCATE events")
        yield test_client


@pytest.fixture
def fetch_events():
    def _fetch():
        with db.connect() as conn:
            return conn.execute(
                "SELECT event_id, event_name, distinct_id, occurred_at, received_at, properties FROM events"
            ).fetchall()

    return _fetch
