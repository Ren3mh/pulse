using System.Text.Json;
using Npgsql;
using Pulse.Api.Sync;

namespace Pulse.Api.Dashboard;

// Everything the /dashboard page shows, read from Postgres in one go. Read-only:
// sync health comes from ops.sync_runs, never from pinging AX or Shopify.
public static class DashboardData
{
    public static readonly Dictionary<string, TimeSpan> Ranges = new()
    {
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
        ["30d"] = TimeSpan.FromDays(30),
    };

    // A source is stale when its last good run is older than two intervals.
    public static readonly TimeSpan StaleAfter = SyncWorker<ISync>.Interval * 2;

    private static readonly string[] Schemas = ["raw_ax", "raw_shopify", "core", "ops"];

    public record Options(bool ShopifyConfigured, string Warehouse);

    public static async Task<object> LoadAsync(NpgsqlConnection db, Options options, DateTimeOffset now,
        TimeSpan range, string? sku, CancellationToken ct = default)
    {
        var sources = new List<object>
        {
            await SourceAsync(db, "ax", configured: true, now, ct),
            await SourceAsync(db, "shopify", options.ShopifyConfigured, now, ct),
        };
        return new
        {
            now,
            intervalMinutes = SyncWorker<ISync>.Interval.TotalMinutes,
            sources,
            tables = await TablesAsync(db, ct),
            stock = await StockAsync(db, options.Warehouse, ct),
            series = new { sku, points = await SeriesAsync(db, options.Warehouse, now - range, sku, ct) },
        };
    }

    // ok: last run worked within StaleAfter. stale: it worked, but longer ago.
    // failing: the last run failed. waiting: no runs yet. off: not configured.
    public static string Status(bool configured, Run? last, DateTimeOffset? lastSuccess, DateTimeOffset now) =>
        !configured ? "off"
        : last is null ? "waiting"
        : !last.Ok ? "failing"
        : now - lastSuccess > StaleAfter ? "stale"
        : "ok";

    public record Run(DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Ok, string? Error, JsonElement Rows);

    private static async Task<object> SourceAsync(NpgsqlConnection db, string source, bool configured,
        DateTimeOffset now, CancellationToken ct)
    {
        var runs = new List<Run>();
        await using (var command = new NpgsqlCommand("""
            SELECT started_at, finished_at, ok, error, rows_written::text FROM ops.sync_runs
            WHERE source = @source ORDER BY started_at DESC LIMIT 10
            """, db))
        {
            command.Parameters.AddWithValue("source", source);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                runs.Add(new Run(reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    JsonDocument.Parse(reader.GetString(4)).RootElement));
        }

        DateTimeOffset? lastSuccess;
        await using (var command = new NpgsqlCommand(
            "SELECT max(finished_at) FROM ops.sync_runs WHERE source = @source AND ok", db))
        {
            command.Parameters.AddWithValue("source", source);
            lastSuccess = await command.ExecuteScalarAsync(ct) is DateTime t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
        }

        var last = runs.FirstOrDefault();
        return new
        {
            name = source,
            configured,
            status = Status(configured, last, lastSuccess, now),
            lastSuccess,
            nextRunAt = configured && last is not null ? last.StartedAt + SyncWorker<ISync>.Interval : (DateTimeOffset?)null,
            runs,
        };
    }

    // Exact row counts: the lab is small enough for count(*).
    private static async Task<List<object>> TablesAsync(NpgsqlConnection db, CancellationToken ct)
    {
        var names = new List<(string Schema, string Name, string Kind)>();
        await using (var command = new NpgsqlCommand("""
            SELECT table_schema, table_name, CASE table_type WHEN 'VIEW' THEN 'view' ELSE 'table' END
            FROM information_schema.tables WHERE table_schema = ANY(@schemas)
            ORDER BY array_position(@schemas, table_schema::text), table_name
            """, db))
        {
            command.Parameters.AddWithValue("schemas", Schemas);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) names.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        var tables = new List<object>();
        foreach (var (schema, name, kind) in names)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".\"{name}\"", db);
            tables.Add(new { schema, name, kind, rows = (long)(await count.ExecuteScalarAsync(ct))! });
        }
        return tables;
    }

    // Gaps compare on_hand at the AX warehouse with Shopify's, which mirrors it:
    // 0 when the two agree, so every gap is real drift. Available is shown as
    // context only, since each side subtracts its own reservations.
    private static async Task<object> StockAsync(NpgsqlConnection db, string warehouse, CancellationToken ct)
    {
        await using var times = new NpgsqlCommand("""
            SELECT max(synced_at) FILTER (WHERE source = 'ax'), max(synced_at) FILTER (WHERE source = 'shopify')
            FROM core.stock
            """, db);
        DateTimeOffset? axAt, shopifyAt;
        await using (var reader = await times.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            axAt = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
            shopifyAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }

        var gaps = new List<object>();
        if (axAt is not null && shopifyAt is not null)
        {
            await using var command = new NpgsqlCommand("""
                WITH s AS (
                    SELECT sku,
                           sum(on_hand)   FILTER (WHERE source = 'ax' AND location = @warehouse) AS ax_on_hand,
                           sum(available) FILTER (WHERE source = 'ax' AND location = @warehouse) AS ax_available,
                           sum(on_hand)   FILTER (WHERE source = 'shopify') AS shopify_on_hand,
                           sum(available) FILTER (WHERE source = 'shopify') AS shopify_available
                    FROM core.stock GROUP BY sku
                )
                SELECT p.sku, p.name,
                       COALESCE(s.ax_on_hand, 0), COALESCE(s.ax_available, 0),
                       COALESCE(s.shopify_on_hand, 0), COALESCE(s.shopify_available, 0)
                FROM core.products p LEFT JOIN s ON s.sku = p.sku
                WHERE p.in_ax AND p.in_shopify
                  AND COALESCE(s.ax_on_hand, 0) <> COALESCE(s.shopify_on_hand, 0)
                ORDER BY abs(COALESCE(s.shopify_on_hand, 0) - COALESCE(s.ax_on_hand, 0)) DESC, p.sku
                """, db);
            command.Parameters.AddWithValue("warehouse", warehouse);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ax = reader.GetDecimal(2);
                var shopify = reader.GetDecimal(4);
                gaps.Add(new
                {
                    sku = reader.GetString(0), name = reader.GetString(1),
                    axOnHand = ax, axAvailable = reader.GetDecimal(3),
                    shopifyOnHand = shopify, shopifyAvailable = reader.GetDecimal(5),
                    gap = shopify - ax, // positive: the webshop offers more than AX has
                });
            }
        }

        return new
        {
            warehouse, axSyncedAt = axAt, shopifySyncedAt = shopifyAt, gaps,
            onlyInAx = await ProductsAsync(db, "in_ax AND NOT in_shopify", ct),
            onlyInShopify = await ProductsAsync(db, "in_shopify AND NOT in_ax", ct),
        };
    }

    private static async Task<List<object>> ProductsAsync(NpgsqlConnection db, string where, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT sku, name FROM core.products WHERE {where} ORDER BY sku", db);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var products = new List<object>();
        while (await reader.ReadAsync(ct))
            products.Add(new { sku = reader.IsDBNull(0) ? null : reader.GetString(0), name = reader.IsDBNull(1) ? null : reader.GetString(1) });
        return products;
    }

    // On hand per sync: the AX warehouse and Shopify, for all SKUs or one.
    private static async Task<object> SeriesAsync(NpgsqlConnection db, string warehouse, DateTimeOffset from,
        string? sku, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT source, synced_at, sum(on_hand) FROM core.stock_snapshots
            WHERE synced_at >= @from AND (source = 'shopify' OR location = @warehouse)
              AND (@sku::text IS NULL OR sku = @sku)
            GROUP BY source, synced_at ORDER BY synced_at
            """, db);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("warehouse", warehouse);
        command.Parameters.AddWithValue("sku", (object?)sku ?? DBNull.Value);
        var points = new Dictionary<string, List<object[]>> { ["ax"] = [], ["shopify"] = [] };
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            points[reader.GetString(0)].Add([reader.GetFieldValue<DateTimeOffset>(1), reader.GetDecimal(2)]);
        return points;
    }
}
