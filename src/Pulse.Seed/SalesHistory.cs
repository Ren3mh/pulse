namespace Pulse.Seed;

public record Customer(string AccountNum, string Name, string Group);

public record OrderLine(string Sku, int Qty, decimal Price);

public record Order(string SalesId, string CustAccount, DateTime Created, OrderLine[] Lines);

// 18 months of made-up AX orders. Each B2B customer follows a pattern the
// later analyses should find; the shop's walk-in sales fill in the rest.
public static class SalesHistory
{
    public const string WalkIn = "20000";

    public static readonly Customer[] Customers =
    [
        new("10001", "Nordlys Forsikring", "FORSIKR"),        // steady
        new("10002", "Hav & Hus Forsikring", "FORSIKR"),      // mix narrows to beds only
        new("10003", "Hotel Fjordgården", "HOTEL"),           // steady, every quarter
        new("10004", "Østby Kommune", "OFFENT"),              // gaps grow, then stops
        new("10005", "Kontorhuset ApS", "ERHVERV"),           // steady
        new("10006", "Tryghed Skadeservice", "FORSIKR"),      // order size shrinks
        new(WalkIn, "Butik – kontant", "DETAIL"),
    ];

    // Customer prices that differ from the list price: account -> (sku -> price).
    public static readonly Dictionary<string, Dictionary<string, decimal>> CustomerPrices = new()
    {
        ["10001"] = new() { ["OSLO3-NAT"] = 10995m, ["OSLO3-GRA"] = 10995m, ["AURORA160-GRA"] = 12495m },
        ["10003"] = new() { ["HOTEL90-HVI"] = 3995m, ["SENGEBORD-GRA"] = 1195m },
    };

    private static readonly string[] InsurancePool =
        ["OSLO3-NAT", "OSLO3-GRA", "OSLO2-GRA", "AURORA160-GRA", "AURORA180-GRA", "EG200-NAT", "ELLA-NAT", "ULD200-GRA"];
    private static readonly string[] Beds = ["AURORA160-GRA", "AURORA160-SAN", "AURORA180-GRA"];
    private static readonly string[] HotelPool = ["HOTEL90-HVI", "SENGEBORD-GRA"];
    private static readonly string[] OfficePool = ["FLEX-SOR", "REOL80-EG", "EG200-NAT"];

    public static List<Order> Generate(DateTime today)
    {
        var random = new Random(42);
        var start = today.AddMonths(-18);
        var orders = new List<(string Account, DateTime Created, OrderLine[] Lines)>();

        void Every(string account, int days, Func<DateTime, double, OrderLine[]> lines,
            Func<double, double>? gapFactor = null, double stopAt = 1.0)
        {
            for (var date = start.AddDays(random.Next(days)); date < today; )
            {
                var progress = (date - start).TotalDays / (today - start).TotalDays;
                if (progress > stopAt) break;
                orders.Add((account, At(date, random), lines(date, progress)));
                date = date.AddDays(days * (gapFactor?.Invoke(progress) ?? 1) + random.Next(-5, 6));
            }
        }

        OrderLine Line(string account, string sku, int maxQty) =>
            new(sku, random.Next(1, maxQty + 1), PriceFor(account, sku));

        OrderLine[] Pick(string account, string[] pool, int count, int maxQty) =>
            pool.OrderBy(_ => random.Next()).Take(count).Select(s => Line(account, s, maxQty)).ToArray();

        Every("10001", 30, (_, _) => Pick("10001", InsurancePool, random.Next(2, 5), 3));
        Every("10002", 35, (_, p) => p < 0.66
            ? Pick("10002", InsurancePool, random.Next(2, 5), 3)
            : Pick("10002", Beds, random.Next(1, 3), 3));
        Every("10003", 90, (_, _) => [Line("10003", "HOTEL90-HVI", 20), Line("10003", "SENGEBORD-GRA", 10)]);
        Every("10004", 40, (_, _) => Pick("10004", OfficePool, random.Next(1, 3), 8),
            gapFactor: p => 1 + 2.5 * p, stopAt: 0.75);
        Every("10005", 45, (_, _) => Pick("10005", OfficePool, random.Next(1, 3), 6));
        Every("10006", 30, (_, p) => Pick("10006", InsurancePool, Math.Max(1, (int)Math.Round(4 - 3 * p)),
            Math.Max(1, (int)Math.Round(4 - 3 * p))));

        // Walk-in: about three sales a week, one to two items each, slow movers rarely.
        var retail = Catalogue.Items.Select(i => i.Variant.Sku).Except(Catalogue.B2BOnly).ToArray();
        for (var date = start; date < today; date = date.AddDays(random.Next(1, 4)))
        {
            var lines = Enumerable.Range(0, random.Next(1, 3))
                .Select(_ => retail[random.Next(retail.Length)])
                .Where(sku => !Catalogue.SlowMovers.Contains(sku) || random.Next(10) == 0)
                .Distinct()
                .Select(sku => Line(WalkIn, sku, 1))
                .ToArray();
            if (lines.Length > 0) orders.Add((WalkIn, At(date, random), lines));
        }

        return orders
            .OrderBy(o => o.Created)
            .Select((o, i) => new Order($"SO-{i + 1:D6}", o.Account, o.Created, o.Lines))
            .ToList();
    }

    public static decimal PriceFor(string account, string sku) =>
        CustomerPrices.TryGetValue(account, out var prices) && prices.TryGetValue(sku, out var price)
            ? price
            : Catalogue.Items.Single(i => i.Variant.Sku == sku).Variant.Price;

    private static DateTime At(DateTime date, Random random) =>
        date.Date.AddHours(random.Next(9, 18)).AddMinutes(random.Next(60));
}
