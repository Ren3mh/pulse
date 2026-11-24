using Pulse.Seed;

namespace Pulse.Api.Tests.Seed;

public class SalesHistoryTests
{
    private static readonly DateTime Today = new(2026, 11, 24);
    private readonly List<Order> _orders = SalesHistory.Generate(Today);

    [Fact]
    public void Is_the_same_every_run()
    {
        var again = SalesHistory.Generate(Today);

        Assert.Equal(
            _orders.Select(o => (o.SalesId, o.Created, o.Lines.Length)),
            again.Select(o => (o.SalesId, o.Created, o.Lines.Length)));
    }

    [Fact]
    public void Covers_eighteen_months_up_to_today()
    {
        Assert.All(_orders, o => Assert.InRange(o.Created, Today.AddMonths(-18), Today.AddDays(1)));
        Assert.True(_orders.Min(o => o.Created) < Today.AddMonths(-17));
    }

    [Fact]
    public void Ostby_kommune_has_stopped_ordering()
    {
        var last = _orders.Where(o => o.CustAccount == "10004").Max(o => o.Created);

        Assert.True(last < Today.AddMonths(-3));
    }

    [Fact]
    public void Hav_og_hus_only_buys_beds_lately()
    {
        var recent = _orders.Where(o => o.CustAccount == "10002" && o.Created > Today.AddMonths(-5));

        Assert.NotEmpty(recent);
        Assert.All(recent.SelectMany(o => o.Lines), l => Assert.StartsWith("AURORA", l.Sku));
    }

    [Fact]
    public void Customer_prices_are_used()
    {
        var hotelLines = _orders.Where(o => o.CustAccount == "10003").SelectMany(o => o.Lines).Where(l => l.Sku == "HOTEL90-HVI");

        Assert.All(hotelLines, l => Assert.Equal(3995m, l.Price));
    }

    [Fact]
    public void Walk_in_never_buys_b2b_only_items()
    {
        var walkIn = _orders.Where(o => o.CustAccount == SalesHistory.WalkIn).SelectMany(o => o.Lines);

        Assert.DoesNotContain(walkIn, l => Catalogue.B2BOnly.Contains(l.Sku));
    }
}
