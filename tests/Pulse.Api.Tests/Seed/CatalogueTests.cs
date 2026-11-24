using Pulse.Seed;

namespace Pulse.Api.Tests.Seed;

public class CatalogueTests
{
    [Fact]
    public void Has_about_thirty_unique_skus()
    {
        var skus = Catalogue.Items.Select(i => i.Variant.Sku).ToList();

        Assert.InRange(skus.Count, 25, 35);
        Assert.Equal(skus.Count, skus.Distinct().Count());
    }

    [Fact]
    public void Skus_fit_ax_item_ids()
    {
        Assert.All(Catalogue.Items, i => Assert.InRange(i.Variant.Sku.Length, 1, 20));
    }

    [Fact]
    public void Gtins_are_unique_valid_in_store_ean13()
    {
        var gtins = Catalogue.Items.Select(i => i.Variant.Gtin).ToList();

        Assert.Equal(gtins.Count, gtins.Distinct().Count());
        Assert.All(gtins, g =>
        {
            Assert.Matches("^2[0-9]{12}$", g);
            Assert.Equal(g[12] - '0', Gtin.CheckDigit(g[..12]));
        });
    }

    [Fact]
    public void Gtin_check_digit_matches_a_known_barcode()
    {
        Assert.Equal(1, Gtin.CheckDigit("400638133393")); // 4006381333931
    }

    [Fact]
    public void Special_skus_exist_in_the_catalogue()
    {
        var skus = Catalogue.Items.Select(i => i.Variant.Sku).ToHashSet();

        Assert.Subset(skus, Catalogue.B2BOnly.Concat(Catalogue.SlowMovers).ToHashSet());
        Assert.Subset(skus, SalesHistory.CustomerPrices.Values.SelectMany(p => p.Keys).ToHashSet());
    }
}
