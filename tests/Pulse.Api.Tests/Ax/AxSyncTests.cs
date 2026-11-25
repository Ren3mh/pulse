using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pulse.Api.Ax;
using Pulse.Seed;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Pulse.Api.Tests.Ax;

public class AxSyncTests : IAsyncLifetime
{
    private static readonly DateTime Today = new(2026, 11, 24);
    private readonly MsSqlContainer _sql = new MsSqlBuilder(AxSchema.Image).Build();
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(Postgres.Image).Build();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero));
    private string _axConnection = "";
    private NpgsqlDataSource _db = null!;
    private AxSync _sync = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _pg.StartAsync());
        await (await AxSchema.ApplyAsync(_sql.GetConnectionString())).DisposeAsync();
        _axConnection = _sql.GetConnectionString().Replace("Database=master", "Database=DynamicsAx");
        await new AxSeeder(_axConnection, "pul").SeedAsync(Today);

        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        _sync = new AxSync(_axConnection, "pul", _db, _clock);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _pg.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Copies_every_table()
    {
        await _sync.RunAsync(CancellationToken.None);

        await using var ax = await AxSchema.ApplyAsync(_axConnection);
        foreach (var table in AxSync.Tables.Keys)
            Assert.Equal(
                await ax.ScalarAsync<int>($"SELECT COUNT(*) FROM {table}"),
                await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM raw_ax.{table.ToLowerInvariant()}"));
        Assert.Equal(
            await ax.ScalarAsync<decimal>("SELECT SUM(LINEAMOUNT) FROM SALESLINE"),
            await _db.ScalarAsync<decimal>("SELECT SUM(lineamount) FROM raw_ax.salesline"));
    }

    [Fact]
    public async Task Rerunning_appends_stock_and_replaces_the_rest()
    {
        await _sync.RunAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(15));

        await _sync.RunAsync(CancellationToken.None);

        await using var ax = await AxSchema.ApplyAsync(_axConnection);
        var stockRows = await ax.ScalarAsync<int>("SELECT COUNT(*) FROM INVENTSUM");
        Assert.Equal(2 * stockRows, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_ax.stock_snapshots"));
        Assert.Equal(2, await _db.ScalarAsync<int>("SELECT COUNT(DISTINCT synced_at) FROM raw_ax.stock_snapshots"));
        Assert.Equal(
            await ax.ScalarAsync<int>("SELECT COUNT(*) FROM SALESLINE"),
            await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_ax.salesline"));
    }

    [Fact]
    public async Task Copies_only_the_configured_company()
    {
        await new AxSeeder(_axConnection, "oth").SeedAsync(Today);

        await _sync.RunAsync(CancellationToken.None);

        Assert.Equal(Catalogue.Items.Count(), await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_ax.inventtable"));
        Assert.Equal(0, await _db.ScalarAsync<int>("SELECT COUNT(*) FROM raw_ax.salestable WHERE dataareaid <> 'pul'"));
    }
}
