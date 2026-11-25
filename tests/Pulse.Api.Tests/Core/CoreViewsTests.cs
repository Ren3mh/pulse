using Pulse.Seed;

namespace Pulse.Api.Tests.Core;

public class CoreViewsTests(LabFixture lab) : IClassFixture<LabFixture>
{
    [Fact]
    public async Task Every_catalogue_sku_is_one_product_found_in_both_systems()
    {
        var gtins = await lab.Db.ByKeyAsync<string>(
            "SELECT sku, gtin FROM core.products WHERE in_ax AND in_shopify AND matched_on = 'sku'");

        Assert.Equal(
            Catalogue.Items.ToDictionary(i => i.Variant.Sku, i => i.Variant.Gtin).OrderBy(x => x.Key),
            gtins.OrderBy(x => x.Key));
        Assert.Equal(Catalogue.Items.Count(), await lab.Db.ScalarAsync<int>("SELECT COUNT(*) FROM core.products"));
    }

    [Fact]
    public async Task Ax_list_price_and_shopify_price_agree()
    {
        Assert.Equal(0, await lab.Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.products WHERE ax_list_price IS DISTINCT FROM shopify_price"));
        Assert.Equal("DRAFT", await lab.Db.ScalarAsync<string>(
            "SELECT shopify_status FROM core.products WHERE sku = 'HOTEL90-HVI'"));
    }

    [Fact]
    public async Task Falls_back_to_gtin_when_the_skus_differ() =>
        await lab.WithChangeAsync("UPDATE raw_shopify.variants SET sku = 'OSLO-3-NATUR' WHERE sku = 'OSLO3-NAT'", async db =>
        {
            Assert.Equal("gtin", await db.ScalarAsync<string>("SELECT matched_on FROM core.products WHERE sku = 'OSLO3-NAT'"));
            Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM core.products WHERE sku = 'OSLO-3-NATUR'"));
            Assert.Equal(1, await db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM core.stock WHERE source = 'shopify' AND sku = 'OSLO3-NAT'"));
        });

    [Fact]
    public async Task An_item_in_one_system_only_still_gets_a_row() =>
        await lab.WithChangeAsync("""
            INSERT INTO raw_shopify.variants (id, product_id, sku, barcode, title, price, inventory_item_id, updated_at, synced_at)
            SELECT 'gid://shopify/ProductVariant/web-only', product_id, 'WEB-ONLY', '2000000000008', 'Web', 99, 'web-only', now(), now()
            FROM raw_shopify.variants LIMIT 1;
            DELETE FROM raw_ax.inventitembarcode WHERE itemid = 'GLOB-MES';
            UPDATE raw_shopify.variants SET sku = NULL, barcode = NULL WHERE sku = 'GLOB-MES';
            """, async db =>
        {
            Assert.False(await db.ScalarAsync<bool>("SELECT in_ax FROM core.products WHERE sku = 'WEB-ONLY'"));
            Assert.False(await db.ScalarAsync<bool>("SELECT in_shopify FROM core.products WHERE sku = 'GLOB-MES'"));
            Assert.Equal(Catalogue.Items.Count() + 2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM core.products"));
        });

    [Fact]
    public async Task Both_systems_count_the_same_warehouse_stock()
    {
        var ax = await lab.Db.ByKeyAsync<int>("SELECT sku, on_hand FROM core.stock WHERE source = 'ax' AND location = 'LAGER'");
        var shopify = await lab.Db.ByKeyAsync<int>("SELECT sku, on_hand FROM core.stock WHERE source = 'shopify'");

        Assert.Equal(ax.OrderBy(x => x.Key), shopify.OrderBy(x => x.Key));
    }

    [Fact]
    public async Task Ax_available_stock_is_on_hand_minus_reserved()
    {
        Assert.Equal(0, await lab.Db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM core.stock WHERE source = 'ax' AND available <> on_hand - reserved"));
        Assert.True(await lab.Db.ScalarAsync<int>("SELECT COUNT(*) FROM core.stock WHERE source = 'ax' AND reserved > 0") > 0);
    }

    [Fact]
    public async Task Stock_shows_only_the_latest_snapshot() =>
        await lab.WithChangeAsync("""
            INSERT INTO raw_ax.stock_snapshots
            SELECT itemid, inventdimid, physicalinvent + 100, reservphysical, availphysical + 100, onorder,
                   dataareaid, modifieddatetime, recid, synced_at + interval '15 minutes'
            FROM raw_ax.stock_snapshots;
            """, async db =>
        {
            Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(DISTINCT synced_at) FROM core.stock_snapshots WHERE source = 'ax'"));
            Assert.Equal(106, await db.ScalarAsync<int>(
                "SELECT on_hand FROM core.stock WHERE source = 'ax' AND sku = 'OSLO3-NAT' AND location = 'LAGER'"));
        });

    [Fact]
    public async Task Orders_come_from_both_systems()
    {
        Assert.Equal(SalesHistory.Generate(LabFixture.Today).Count,
            await lab.Db.ScalarAsync<int>("SELECT COUNT(*) FROM core.orders WHERE source = 'ax'"));
        Assert.Equal(2, await lab.Db.ScalarAsync<int>("SELECT COUNT(*) FROM core.orders WHERE source = 'shopify'"));
        Assert.Equal("cancelled", await lab.Db.ScalarAsync<string>("SELECT status FROM core.orders WHERE order_number = '#1002'"));
        Assert.Equal("Nordlys Forsikring", await lab.Db.ScalarAsync<string>(
            "SELECT DISTINCT customer_name FROM core.orders WHERE customer_id = '10001'"));
    }

    [Fact]
    public async Task Ax_order_totals_are_the_sum_of_their_lines()
    {
        Assert.Equal(0, await lab.Db.ScalarAsync<int>("""
            SELECT COUNT(*) FROM core.orders o
            WHERE o.source = 'ax'
              AND o.total <> (SELECT sum(l.amount) FROM core.order_lines l WHERE l.source = 'ax' AND l.order_id = o.order_id)
            """));
    }

    [Fact]
    public async Task Shopify_order_lines_get_the_matched_sku() =>
        await lab.WithChangeAsync("UPDATE raw_shopify.variants SET sku = 'OSLO-3-NATUR' WHERE sku = 'OSLO3-NAT'", async db =>
        {
            Assert.Equal("OSLO3-NAT", await db.ScalarAsync<string>(
                "SELECT sku FROM core.order_lines WHERE line_id = 'gid://shopify/LineItem/9001'"));
            Assert.Equal("ELLA-NAT", await db.ScalarAsync<string>(
                "SELECT sku FROM core.order_lines WHERE line_id = 'gid://shopify/LineItem/9002'"));
        });

    [Fact]
    public async Task The_churned_customer_stops_ordering()
    {
        var lastOrder = await lab.Db.ScalarAsync<DateTime>("SELECT max(created_at) FROM core.orders WHERE customer_id = '10004'");

        Assert.True(lastOrder < LabFixture.Today.AddMonths(-3));
    }
}
