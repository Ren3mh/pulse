using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pulse.Api.Shopify;
using Pulse.Seed;
using Testcontainers.PostgreSql;

namespace Pulse.Api.Tests.Shopify;

public class ShopifySyncTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(Postgres.Image).Build();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero));
    private NpgsqlDataSource _db = null!;
    private ShopifySync _sync = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        _sync = new ShopifySync(new RecordedShopify().Client(_clock), _db, _clock);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Copies_the_catalogue_with_skus_and_barcodes()
    {
        await _sync.RunAsync(CancellationToken.None);

        Assert.Equal(Catalogue.Products.Length, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.products"));
        var barcodes = await _db.ByKeyAsync<string>("SELECT sku, barcode FROM raw_shopify.variants");
        Assert.Equal(
            Catalogue.Items.ToDictionary(i => i.Variant.Sku, i => i.Variant.Gtin).OrderBy(x => x.Key),
            barcodes.OrderBy(x => x.Key));
    }

    [Fact]
    public async Task Snapshots_stock_per_sku()
    {
        await _sync.RunAsync(CancellationToken.None);

        var stock = await _db.ByKeyAsync<int>("SELECT sku, available FROM raw_shopify.stock_snapshots");
        Assert.Equal(
            Catalogue.Items.ToDictionary(i => i.Variant.Sku, i => i.Variant.Stock).OrderBy(x => x.Key),
            stock.OrderBy(x => x.Key));
    }

    [Fact]
    public async Task Reads_every_order_page()
    {
        await _sync.RunAsync(CancellationToken.None);

        Assert.Equal(2, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.orders"));
        Assert.Equal(3, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.order_lines"));
        Assert.Equal(16585m, await _db.ScalarAsync<decimal>("SELECT total_price FROM raw_shopify.orders WHERE name = '#1001'"));
        Assert.Equal(1, await _db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM raw_shopify.orders WHERE name = '#1002' AND customer_id IS NULL AND cancelled_at IS NOT NULL"));
    }

    [Fact]
    public async Task Rerunning_appends_stock_and_replaces_the_rest()
    {
        await _sync.RunAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(15));

        await _sync.RunAsync(CancellationToken.None);

        var variants = Catalogue.Items.Count();
        Assert.Equal(variants, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.variants"));
        Assert.Equal(2, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.orders"));
        Assert.Equal(3, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.order_lines"));
        Assert.Equal(2 * variants, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_shopify.stock_snapshots"));
        Assert.Equal(2, await _db.ScalarAsync<int>("SELECT COUNT(DISTINCT synced_at) FROM raw_shopify.stock_snapshots"));
    }
}
