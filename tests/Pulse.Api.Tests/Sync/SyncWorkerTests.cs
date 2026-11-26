using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pulse.Api.Core;
using Pulse.Api.Sync;
using Testcontainers.PostgreSql;

namespace Pulse.Api.Tests.Sync;

public class SyncWorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(Postgres.Image).Build();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
    private NpgsqlDataSource _db = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await Schema.CreateAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    private class FakeSync(FakeTimeProvider clock, Exception? failWith = null) : ISync
    {
        public string Source => "fake";

        public Task<IReadOnlyDictionary<string, int>> RunAsync(CancellationToken ct)
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            if (failWith is not null) throw failWith;
            return Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int> { ["raw_fake.items"] = 30 });
        }
    }

    private Task RunAsync(FakeSync sync) =>
        new SyncWorker<FakeSync>(sync, _db, _clock, NullLogger<SyncWorker<FakeSync>>.Instance).RunOnceAsync(CancellationToken.None);

    [Fact]
    public async Task A_good_run_is_recorded_with_its_row_counts()
    {
        await RunAsync(new FakeSync(_clock));

        Assert.Equal("fake|t||30|3", await _db.ScalarAsync<string>("""
            SELECT concat_ws('|', source, ok, coalesce(error, ''), rows_written->>'raw_fake.items',
                             extract(epoch FROM finished_at - started_at)::int)
            FROM ops.sync_runs
            """));
    }

    [Fact]
    public async Task A_failed_run_is_recorded_with_its_error()
    {
        await RunAsync(new FakeSync(_clock, new InvalidOperationException("AX is down")));

        Assert.Equal("f|InvalidOperationException: AX is down|{}", await _db.ScalarAsync<string>(
            "SELECT concat_ws('|', ok, error, rows_written::text) FROM ops.sync_runs"));
    }

    [Fact]
    public async Task A_run_still_finishes_when_it_cannot_be_recorded()
    {
        await using var broken = NpgsqlDataSource.Create("Host=localhost;Port=1;Timeout=1");
        await new SyncWorker<FakeSync>(new FakeSync(_clock), broken, _clock, NullLogger<SyncWorker<FakeSync>>.Instance)
            .RunOnceAsync(CancellationToken.None);
    }
}
