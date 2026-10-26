# pulse prototype (Python)

Phase 1 of pulse, built in Python because the cloud workspace it was made in
could not reach Microsoft's .NET and NuGet servers. Same event format
([docs/event-format.md](../docs/event-format.md)), same Postgres design, so it
doubles as a reference for the C# version in `src/`.

The test app is **Nordlys Design**, a fictional design-furniture shop. Every
click sends an event, and a side panel shows what was sent.

## Run it

```sh
# Postgres: the repo's compose file, or any local Postgres with a pulse user
docker compose up -d
psql -h localhost -U pulse -c "CREATE DATABASE pulse_test"   # for the tests

cd prototype
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
.venv/bin/python -m pytest              # 19 tests, against real Postgres
.venv/bin/uvicorn pulse.app:app --reload
```

Open http://localhost:8000 and click around, then look at the data:

```sh
psql -h localhost -U pulse -c "SELECT event_name, properties, received_at - occurred_at AS lag FROM events"
```

## Layout

| File | Job |
| --- | --- |
| `schema.sql` | `events` and `products` tables. Applied on startup, safe to rerun. |
| `seed_products.sql` | 12 invented products. Some lack German text or dimensions on purpose. |
| `pulse/events.py` | The v1 event format as a Pydantic model: all validation lives here. |
| `pulse/store.py` | `save_event()`, the only code that writes events. Swap it for a queue in phase 4. |
| `pulse/catalog.py` | Read-only product queries. |
| `pulse/app.py` | HTTP routes: `/health`, `POST /events`, `/products`, `/products/{id}`, `/shop/`. |
| `shop/track.js` | The shop's only way to send events. |
| `shop/index.html` | The test shop. |
| `tests/` | API tests against a real `pulse_test` database. |

## Decisions

- **Idempotent intake.** `POST /events` returns 202 and `"duplicate": true` when
  the `event_id` is already stored. A retry never creates a second row.
- **Strict format.** Unknown fields (including a client-sent `received_at`),
  timestamps without a timezone, nested properties and non-snake_case names get a 422.
- **No personal data.** Events carry an anonymous `distinct_id` only.
- **One connection per request.** Fine for now; pooling belongs to phase 4.

## Next (from the learning path)

- MCP server, read-only: `search_products`, `catalog_gaps`, `count_events`,
  `get_user_timeline`, `ingest_health`.
- Insights: SQL models over `events` joined with `products`.
