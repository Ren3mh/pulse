using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pulse.Api.Ax;
using Pulse.Api.Core;
using Pulse.Api.Shopify;
using Pulse.Api.Tests.Ax;
using Pulse.Api.Tests.Shopify;
using Pulse.Seed;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Pulse.Api.Tests.Core;

// The whole lab once per test class: AX seeded, both syncs run (Shopify from
// the recorded responses) and the core views created. Tests that change rows
// do it in a transaction they roll back.
public class LabFixture : IAsyncLifetime
{
    public static readonly DateTime Today = new(2026, 11, 24);

    private readonly MsSqlContainer _sql = new MsSqlBuilder(AxSchema.Image).Build();
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(Postgres.Image).Build();

    public NpgsqlDataSource Db { get; private set; } = null!;

    public string ConnectionString => _pg.GetConnectionString();

    // When the fixture's syncs ran.
    public static readonly DateTimeOffset SyncedAt = new(Today.AddHours(8), TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _pg.StartAsync());
        await (await AxSchema.ApplyAsync(_sql.GetConnectionString())).DisposeAsync();
        var ax = _sql.GetConnectionString().Replace("Database=master", "Database=DynamicsAx");
        await new AxSeeder(ax, "pul").SeedAsync(Today);

        Db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        var clock = new FakeTimeProvider(SyncedAt);
        await Schema.CreateAsync(Db);
        await new AxSync(ax, "pul", Db, clock).RunAsync(CancellationToken.None);
        await new ShopifySync(new RecordedShopify().Client(clock), Db, clock).RunAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _pg.DisposeAsync().AsTask());
    }

    // Runs `change`, then `check`, on one connection, and undoes the change.
    public async Task WithChangeAsync(string change, Func<NpgsqlConnection, Task> check)
    {
        await using var connection = await Db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(change, connection, transaction))
            await command.ExecuteNonQueryAsync();
        await check(connection);
        await transaction.RollbackAsync();
    }
}

public static class ConnectionQueries
{
    public static async Task<T> ScalarAsync<T>(this NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
