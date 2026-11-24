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
docker compose up -d        # Postgres on localhost:5432
dotnet run --project src/Pulse.Api
dotnet test
```

## Docs

- [Event format](docs/event-format.md): kept for future webshop events
  (e.g. product views); not part of phase 1.
