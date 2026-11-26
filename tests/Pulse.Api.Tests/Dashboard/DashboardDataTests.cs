using System.Text.Json;
using Npgsql;
using Pulse.Api.Dashboard;
using Pulse.Api.Tests.Core;

namespace Pulse.Api.Tests.Dashboard;

public class DashboardDataTests(LabFixture lab) : IClassFixture<LabFixture>
{
    private static readonly DashboardData.Options Options = new(ShopifyConfigured: true, Warehouse: "LAGER");
    private static readonly DateTimeOffset Now = LabFixture.SyncedAt.AddMinutes(10);

    // The response as the page sees it.
    private static async Task<JsonElement> LoadAsync(NpgsqlConnection db, string? sku = null,
        DashboardData.Options? options = null) =>
        JsonSerializer.SerializeToElement(await DashboardData.LoadAsync(db, options ?? Options, Now, TimeSpan.FromDays(7), sku),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<JsonElement> LoadAsync(string? sku = null)
    {
        await using var db = await lab.Db.OpenConnectionAsync();
        return await LoadAsync(db, sku);
    }

    [Fact]
    public async Task No_gaps_when_the_warehouse_agrees_even_though_ax_has_shop_stock_too()
    {
        var stock = (await LoadAsync()).GetProperty("stock");

        Assert.Equal(0, stock.GetProperty("gaps").GetArrayLength());
        Assert.Equal(0, stock.GetProperty("onlyInAx").GetArrayLength());
        Assert.Equal(0, stock.GetProperty("onlyInShopify").GetArrayLength());
        Assert.True(await lab.Db.ScalarAsync<int>("SELECT COUNT(*) FROM core.stock WHERE source = 'ax' AND location <> 'LAGER' AND on_hand > 0") > 0);
    }

    [Fact]
    public async Task A_shopify_count_that_drifts_from_ax_is_a_signed_gap() =>
        await lab.WithChangeAsync("""
            UPDATE raw_shopify.stock_snapshots SET on_hand = on_hand + 3 WHERE sku = 'OSLO3-NAT';
            UPDATE raw_shopify.stock_snapshots SET on_hand = on_hand - 1 WHERE sku = 'GLOB-MES';
            """, async db =>
        {
            var gaps = (await LoadAsync(db)).GetProperty("stock").GetProperty("gaps").EnumerateArray()
                .Select(g => (g.GetProperty("sku").GetString(), g.GetProperty("gap").GetDecimal())).ToList();

            Assert.Equal([("OSLO3-NAT", 3m), ("GLOB-MES", -1m)], gaps);
        });

    [Fact]
    public async Task Products_in_one_system_only_are_listed_apart() =>
        await lab.WithChangeAsync("UPDATE raw_shopify.variants SET sku = NULL, barcode = NULL WHERE sku = 'GLOB-MES'", async db =>
        {
            var stock = (await LoadAsync(db)).GetProperty("stock");

            Assert.Equal("GLOB-MES", stock.GetProperty("onlyInAx")[0].GetProperty("sku").GetString());
            Assert.Equal(1, stock.GetProperty("onlyInShopify").GetArrayLength());
            Assert.DoesNotContain(stock.GetProperty("gaps").EnumerateArray(), g => g.GetProperty("sku").GetString() == "GLOB-MES");
        });

    [Fact]
    public async Task Sources_show_their_last_runs_and_status() =>
        await lab.WithChangeAsync("""
            INSERT INTO ops.sync_runs (source, started_at, finished_at, ok, error, rows_written) VALUES
              ('ax', '2026-11-24 08:00Z', '2026-11-24 08:00:02Z', true, NULL, '{"raw_ax.inventtable": 30}'),
              ('shopify', '2026-11-24 07:45Z', '2026-11-24 07:45:05Z', true, NULL, '{}'),
              ('shopify', '2026-11-24 08:00Z', '2026-11-24 08:00:01Z', false, 'HttpRequestException: 401', '{}');
            """, async db =>
        {
            var sources = (await LoadAsync(db)).GetProperty("sources");
            var ax = sources[0];
            var shopify = sources[1];

            Assert.Equal("ok", ax.GetProperty("status").GetString());
            Assert.Equal(30, ax.GetProperty("runs")[0].GetProperty("rows").GetProperty("raw_ax.inventtable").GetInt32());
            Assert.Equal(LabFixture.SyncedAt.AddMinutes(15), ax.GetProperty("nextRunAt").GetDateTimeOffset());
            Assert.Equal("failing", shopify.GetProperty("status").GetString());
            Assert.Equal("HttpRequestException: 401", shopify.GetProperty("runs")[0].GetProperty("error").GetString());
            Assert.Equal(new DateTimeOffset(2026, 11, 24, 7, 45, 5, TimeSpan.Zero), shopify.GetProperty("lastSuccess").GetDateTimeOffset());

            var off = await LoadAsync(db, options: Options with { ShopifyConfigured = false });
            Assert.Equal("off", off.GetProperty("sources")[1].GetProperty("status").GetString());
        });

    [Theory]
    [InlineData(true, true, 10, "ok")]
    [InlineData(true, true, 31, "stale")]
    [InlineData(true, false, 10, "failing")]
    [InlineData(true, null, 0, "waiting")]
    [InlineData(false, true, 10, "off")]
    public void Status_follows_the_last_run(bool configured, bool? lastOk, int minutesAgo, string expected)
    {
        var at = Now.AddMinutes(-minutesAgo);
        var last = lastOk is null ? null : new DashboardData.Run(at, at, lastOk.Value, null, default);

        Assert.Equal(expected, DashboardData.Status(configured, last, lastOk == true ? at : null, Now));
    }

    [Fact]
    public async Task Row_counts_cover_every_table_and_view()
    {
        var tables = (await LoadAsync()).GetProperty("tables").EnumerateArray()
            .ToDictionary(t => t.GetProperty("schema").GetString() + "." + t.GetProperty("name").GetString(), t => t.GetProperty("rows").GetInt64());

        Assert.Equal(await lab.Db.ScalarAsync<long>("SELECT COUNT(*) FROM raw_ax.inventtable"), tables["raw_ax.inventtable"]);
        Assert.Equal(await lab.Db.ScalarAsync<long>("SELECT COUNT(*) FROM core.products"), tables["core.products"]);
        Assert.Contains("ops.sync_runs", tables.Keys);
        Assert.Contains("raw_shopify.orders", tables.Keys);
    }

    [Fact]
    public async Task The_chart_sums_warehouse_stock_or_shows_one_sku()
    {
        var totals = (await LoadAsync()).GetProperty("series").GetProperty("points");
        var one = (await LoadAsync("OSLO3-NAT")).GetProperty("series").GetProperty("points");

        var warehouse = await lab.Db.ScalarAsync<decimal>("SELECT sum(on_hand) FROM core.stock WHERE source = 'ax' AND location = 'LAGER'");
        Assert.Equal(warehouse, totals.GetProperty("ax")[0][1].GetDecimal());
        Assert.Equal(warehouse, totals.GetProperty("shopify")[0][1].GetDecimal());
        Assert.Equal(await lab.Db.ScalarAsync<decimal>("SELECT on_hand FROM core.stock WHERE source = 'ax' AND location = 'LAGER' AND sku = 'OSLO3-NAT'"),
            one.GetProperty("ax")[0][1].GetDecimal());
    }
}
