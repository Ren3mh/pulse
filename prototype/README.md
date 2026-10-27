# pulse prototype (Python)

A working version of pulse in Python, built in a cloud workspace that could not
reach Microsoft's .NET and NuGet servers. It has three parts:

1. **Event API and test shop.** Nordlys Design, a fictional design-furniture
   shop, sends events in the v1 format ([docs/event-format.md](../docs/event-format.md))
   to a FastAPI service that stores them in Postgres.
2. **dbt layer.** Raw events become four documented marts: product performance,
   search demand, catalogue gaps and a daily funnel.
3. **MCP server.** Gives an AI agent controlled context: it can read the marts
   and their dbt documentation, and nothing else.

Diagrams of all three: [docs/architecture.md](../docs/architecture.md).
A real model using the marts: [docs/agent-session-example.md](../docs/agent-session-example.md).

## Setup

```sh
docker compose up -d                                    # Postgres (repo root)
psql -h localhost -U pulse -c "CREATE DATABASE pulse_test"
sudo -u postgres psql -d pulse -f prototype/analytics/setup_roles.sql   # agent role, once

cd prototype
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

## 1. Shop and event API

```sh
.venv/bin/uvicorn pulse.app:app --reload     # http://localhost:8000
```

Click around the shop; each click is listed under "Events sent" and stored in
`events`. To get four weeks of realistic data instead:

```sh
.venv/bin/python -m scripts.simulate_traffic   # ~2,300 events, fixed seed
```

The simulation plants two patterns for the marts to find: searches for things the
shop can't find, and lower conversion on products without a Danish text.

## 2. dbt marts

```sh
cd analytics
../.venv/bin/dbt build --profiles-dir .      # models + 24 tests
../.venv/bin/dbt docs generate --profiles-dir . && ../.venv/bin/dbt docs serve --profiles-dir .
```

| Layer | Schema | What's in it | Agent access |
| --- | --- | --- | --- |
| Raw | `public` | `events`, `products` as written by the API | None |
| Staging | `staging` | Typed, cleaned views | None |
| Marts | `marts` | Four documented tables, no visitor ids | SELECT only |

Every mart and column has a description in `models/marts/_marts.yml`. Those
descriptions are the agent's context, so writing them is part of building a mart.

## 3. MCP server

```sh
.venv/bin/python -m scripts.demo_agent_session    # scripted session, prints Markdown
```

Tools: `list_marts`, `describe_mart`, `query_mart`. There is no free-form SQL.

To use it from Claude Code, start `claude` in the repo root. It reads
[.mcp.json](../.mcp.json) and asks once to approve the `pulse-marts` server. The
example in docs/ was made with:

```sh
claude -p "Which product texts should we write first for the new German webshop, and what are customers searching for that they cannot find?" \
  --mcp-config .mcp.json --strict-mcp-config \
  --allowedTools "mcp__pulse-marts__list_marts,mcp__pulse-marts__describe_mart,mcp__pulse-marts__query_mart"
```

### How access is controlled

Each layer would stop a mistake on its own:

| Layer | Rule | Where |
| --- | --- | --- |
| dbt | Only models with `meta.agent_exposed: true` are offered | `_marts.yml` |
| dbt | Only documented columns can be selected, filtered or sorted | `pulse_mcp/marts.py` |
| dbt | Search terms need 5+ different visitors to appear | `mart_search_demand.sql` |
| MCP | Unknown tool arguments are refused, not ignored | `pulse_mcp/server.py` |
| MCP | Names quoted as identifiers, values as parameters, max 100 rows | `pulse_mcp/marts.py` |
| Postgres | Role can SELECT on `marts` only; read-only transactions; 5 s timeout | `setup_roles.sql`, `dbt_project.yml` |

## Tests

```sh
.venv/bin/python -m pytest      # 38 tests against real Postgres (pulse_test)
```

The agent-access tests build the marts in `pulse_test` with dbt, then check that
the agent role can query marts, can't read raw or staging tables, can't write,
and never sees rare searches.

## Layout

| Path | Job |
| --- | --- |
| `pulse/` | Event API: `events.py` (v1 format), `store.py` (the only writer), `catalog.py`, `app.py` |
| `shop/` | The test shop; `track.js` is its only way to send events |
| `schema.sql`, `seed_products.sql` | Raw tables and 12 invented products with deliberate gaps |
| `analytics/` | dbt project: `models/staging`, `models/marts`, `tests/`, `setup_roles.sql` |
| `pulse_mcp/` | MCP server: `marts.py` (all access rules), `server.py` (tools) |
| `scripts/` | `simulate_traffic.py`, `demo_agent_session.py` |
| `tests/` | API, product, mart-access and MCP server tests |
