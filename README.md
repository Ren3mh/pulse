# pulse

A lab that mirrors a furniture retailer's setup: a Shopify store, a
Dynamics AX 2009 database and Claude connected to both. Connectors pull
products, stock, prices and orders into one Postgres model, and an MCP
server lets Claude query it.

It starts as a safe place to practise before touching production, and is
built so it can grow into a sturdier, version-controlled replacement for
loose Claude/Lovable solutions.

## Plan

Sources:
- **Shopify dev store** (free, no expiry), read through the Admin GraphQL API.
- **AX 2009 emulator**, SQL Server in Docker with `InventTable`, `InventSum`
  and `PriceDiscTable`.
- **Competitor prices and trends**: already exist at the company; imported
  later rather than rebuilt.

Outputs, in order:
1. **MCP server** with read-only tools over the combined data.
2. Slow-moving stock: AX stock against sales across all channels.
3. B2B churn: customers drifting from their own order rhythm, size or mix.
4. Price alerts.

First steps:
1. Free dev store, plus a seed script that creates the same ~30 furniture
   products (same SKUs, with GTIN/brand/material) in Shopify and AX.
2. App in the Shopify Dev Dashboard, fetching its token automatically.
3. AX emulator in Docker.
4. Ask for a schema extract of the real AX.
5. MCP server with read-only tools.

## Run locally

```sh
docker compose up -d --build   # Postgres :5432, AX 2009 emulator :1433, Pulse.Api :5099
docker compose logs -f api     # watch the syncs
dotnet test
```

`scripts/pulse-watch` opens a Hyprland workspace with the live logs of `api`,
`postgres` and `ax`, and a status pane (`scripts/pulse-status`: health, last
sync, row counts). Run it again to jump back there.

Each morning, `/today` in Claude Code gives a short brief: where the project
stands, what to start, and what to do next. `scripts/pulse-morning` opens
Claude with it once a day; an Omarchy `post-boot` hook can run that at login
(`omarchy hook install post-boot <script that runs it>`).

To work on the API itself, stop its container (`docker compose stop api`)
and run it with `dotnet run --project src/Pulse.Api` instead.

`Pulse.Api` copies AX and Shopify into Postgres at startup and then every
15 minutes: `raw_ax.*` mirrors the AX tables, `raw_shopify.*` the webshop.
Stock is appended to each schema's `stock_snapshots` rather than replaced,
so its history builds up. Without Shopify credentials only AX is synced.
On top sit the `core` views (`core.products`, `core.stock`,
`core.stock_snapshots`, `core.orders`, `core.order_lines`): AX and Shopify
combined per SKU, with GTIN as the fallback. Pulse answers from these.

Shopify credentials live in user-secrets for `dotnet run`, and in `.env`
for the `api` container (both never in git):

```sh
dotnet user-secrets set "Shopify:StoreDomain" "<store>.myshopify.com" --project src/Pulse.Api
dotnet user-secrets set "Shopify:ClientId" "<id>" --project src/Pulse.Api
dotnet user-secrets set "Shopify:ClientSecret" "<secret>" --project src/Pulse.Api
```

`.env` holds the same three, with `__` for `:`:

```sh
Shopify__StoreDomain=<store>.myshopify.com
Shopify__ClientId=<id>
Shopify__ClientSecret=<secret>
```

Fill both systems with the same furniture catalogue (safe to rerun):

```sh
dotnet run --project src/Pulse.Seed            # both
dotnet run --project src/Pulse.Seed -- ax      # AX only
dotnet run --project src/Pulse.Seed -- shopify # Shopify only
```

AX emulator in Rider: SQL Server, `localhost:1433`, user `sa`, password
`Pulse_local_1!`, database `DynamicsAx`, company (`DATAAREAID`) `pul`.

## Docs

- [Tours](docs/tours/index.html): a history of finished work, each run and
  shown with diagrams and real output. Open in a browser.
- [Event format](docs/event-format.md): kept for future webshop events
  (e.g. product views); not part of phase 1.
