using Pulse.Api.Tests.Ax;
using Pulse.Seed;
using Testcontainers.MsSql;

namespace Pulse.Api.Tests.Seed;

public class AxSeederTests : IAsyncLifetime
{
    private static readonly DateTime Today = new(2026, 11, 24);
    private readonly MsSqlContainer _sql = new MsSqlBuilder(AxSchema.Image).Build();
    private string _axConnection = "";

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await (await AxSchema.ApplyAsync(_sql.GetConnectionString())).DisposeAsync();
        _axConnection = _sql.GetConnectionString().Replace("Database=master", "Database=DynamicsAx");
    }

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    [Fact]
    public async Task Seeds_every_item_customer_and_order()
    {
        await new AxSeeder(_axConnection, "pul").SeedAsync(Today);

        await using var db = await AxSchema.ApplyAsync(_axConnection);
        Assert.Equal(Catalogue.Items.Count(), await db.ScalarAsync<int>("SELECT COUNT(*) FROM INVENTTABLE"));
        Assert.Equal(SalesHistory.Customers.Length, await db.ScalarAsync<int>("SELECT COUNT(*) FROM CUSTTABLE"));
        Assert.Equal(SalesHistory.Generate(Today).Count, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SALESTABLE"));
    }

    [Fact]
    public async Task Rerunning_gives_the_same_data_not_duplicates()
    {
        var seeder = new AxSeeder(_axConnection, "pul");
        await seeder.SeedAsync(Today);
        await using var db = await AxSchema.ApplyAsync(_axConnection);
        var lines = await db.ScalarAsync<int>("SELECT COUNT(*) FROM SALESLINE");

        await new AxSeeder(_axConnection, "pul").SeedAsync(Today);

        Assert.Equal(lines, await db.ScalarAsync<int>("SELECT COUNT(*) FROM SALESLINE"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) - COUNT(DISTINCT RECID) FROM SALESLINE"));
    }

    [Fact]
    public async Task Available_stock_is_physical_minus_reserved()
    {
        await new AxSeeder(_axConnection, "pul").SeedAsync(Today);

        await using var db = await AxSchema.ApplyAsync(_axConnection);
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM INVENTSUM WHERE AVAILPHYSICAL <> PHYSICALINVENT - RESERVPHYSICAL OR AVAILPHYSICAL < 0"));
        Assert.True(await db.ScalarAsync<int>("SELECT COUNT(*) FROM INVENTSUM WHERE RESERVPHYSICAL > 0") > 0);
    }
}
