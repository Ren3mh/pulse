"""Where events go. The rest of the app only calls save_event().

This is the "one small module" from the plan: in phase 4, save_event()
can publish to a queue instead of writing to Postgres, and nothing else changes.
"""

from psycopg.types.json import Jsonb

from pulse.db import connect
from pulse.events import Event


def save_event(event: Event) -> bool:
    """Store an event. Returns False if this event_id was already stored."""
    with connect() as conn:
        cursor = conn.execute(
            """
            INSERT INTO events (event_id, version, event_name, distinct_id, occurred_at, properties)
            VALUES (%s, %s, %s, %s, %s, %s)
            ON CONFLICT (event_id) DO NOTHING
            """,
            (
                event.event_id,
                event.version,
                event.event_name,
                event.distinct_id,
                event.occurred_at,
                Jsonb(event.properties),
            ),
        )
        return cursor.rowcount == 1
