# Handoff: pulse prototype

Context for continuing work on this branch (`prototype/python-phase1`) in a new
Claude Code session. Read this first, then `prototype/README.md` and
`docs/architecture.md`.

## What exists

pulse is a homemade product-analytics pipeline (a learning project for data
engineering, DevOps and maintainable code). This branch adds a working Python
prototype next to the C# skeleton in `src/`, which is unchanged.

| Part | Where | State |
| --- | --- | --- |
| Event API (FastAPI) + test shop "Nordlys Design" | `prototype/pulse/`, `prototype/shop/` | Done, tested |
| Traffic simulation (~2,300 events, fixed seed) | `prototype/scripts/simulate_traffic.py` | Done |
| dbt project: staging + 4 documented marts | `prototype/analytics/` | Done, 24 dbt tests |
| MCP server giving an agent read access to marts only | `prototype/pulse_mcp/`, `.mcp.json` | Done, tested with a real model |
| Diagrams (8, Mermaid) | `docs/architecture.md` | Done |
| Real agent run, step by step | `docs/agent-session-example.md` | Done |

38 pytest tests pass against real Postgres. GitHub CI only builds the C# part.

## Why it's Python

The cloud workspace where this was built could not reach Microsoft's .NET and
NuGet servers. Python was planned for the data side anyway (dbt, pipelines).
The event format and database design match the C# plan, so a C# port of the API
later is a straight translation.

## Key decisions

- **Event format v1** (`docs/event-format.md`) is enforced in `pulse/events.py`:
  unknown fields, client-sent `received_at`, timezone-less timestamps, nested
  properties and non-snake_case names all get a 422.
- **One writer:** `pulse/store.py` `save_event()` is the only code that writes
  events (`ON CONFLICT DO NOTHING` on `event_id`, so retries are safe). Phase 4
  swaps it for a queue.
- **Schemas:** `public` (raw, written by the API), `staging` (dbt views),
  `marts` (dbt tables). A custom `generate_schema_name` macro keeps the names exact.
- **Controlled agent context:** the MCP server only offers models with
  `meta.agent_exposed: true` and only documented columns, read from dbt's
  `manifest.json`. The dbt descriptions are the model's context.
- **Agent role:** `pulse_agent` (created by `analytics/setup_roles.sql`) can only
  SELECT on `marts`, runs read-only transactions and has a 5 s statement timeout.
  dbt grants SELECT per mart (`+grants`) and USAGE on the schema (`on-run-end`).
- **Privacy:** `mart_search_demand` only includes terms searched by 5+
  distinct visitors (`var('search_min_visitors')`). No mart contains visitor ids.
- **Strict tool arguments:** unknown MCP tool arguments are refused, not ignored.
  A real model once sent `{"query": ...}` instead of `filters`; the SDK silently
  dropped it and returned unfiltered rows. See `strict_tool()` in `pulse_mcp/server.py`.
- **Simulated data has planted patterns:** zero-result searches ("egg chair",
  "outdoor", "oak", Danish "sofabord") and lower conversion for products
  without a Danish text. Never present them as real findings.

## Gotchas

- `mcp` is 2.x: `FastMCP` is now `MCPServer` (`mcp.server.mcpserver`). Raise
  `ToolError` for errors the model should read; other exceptions become a
  generic "Error executing tool".
- pip once resolved `dbt-core` to a beta; versions are pinned in
  `prototype/requirements.txt` (dbt-core 1.12.6, dbt-postgres 1.11.0).
- dbt 1.10+ syntax: `data_tests:` and `arguments:` for generic tests; model
  `meta` under `config:`.
- `setup_roles.sql` needs a superuser. With the repo's `compose.yaml`, the
  `pulse` user is one, so `psql -h localhost -U pulse -d pulse -f prototype/analytics/setup_roles.sql` works.
- Tests need a `pulse_test` database. The mart tests run simulation + `dbt build`
  into it (about 10 s).
- Built and tested on Postgres 16; `compose.yaml` uses 18.

## Open items

- [ ] Read the code line by line before merging to `main` (project rule).
- [ ] CI job for the prototype: Postgres service, `pip install`, `dbt build`, `pytest`.
- [ ] Small eval harness: ~10 questions with known answers from the marts,
      2-3 variants of server instructions/tool descriptions, scored on
      correctness, tool errors and cost via `claude -p --output-format json`.
- [ ] Update the learning-path doc with what was built and what changed.
- [ ] Not built from the original MCP plan: `ingest_health`; `get_user_timeline`
      is deliberately left out, since per-visitor data conflicts with the
      controlled-context design.

## Working rules

- Never merge code I can't explain line by line.
- Debug by hand first, then ask the agent.
