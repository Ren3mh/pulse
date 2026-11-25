using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Pulse.Api.Sync;

// One source copied into Postgres. Each run reads everything it needs again.
public interface ISync
{
    // Short name stored in ops.sync_runs, e.g. "ax".
    string Source { get; }

    // Returns the rows written per target table, e.g. "raw_ax.inventtable" -> 30.
    Task<IReadOnlyDictionary<string, int>> RunAsync(CancellationToken ct);
}

// Runs a sync at startup and then every 15 minutes. A failed run is logged
// and retried on the next tick, so one bad run doesn't stop the worker.
// Every run, good or bad, gets a row in ops.sync_runs.
public class SyncWorker<T>(T sync, NpgsqlDataSource postgres, TimeProvider clock, ILogger<SyncWorker<T>> logger)
    : BackgroundService where T : ISync
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var startedAt = clock.GetUtcNow();
        IReadOnlyDictionary<string, int> rows = new Dictionary<string, int>();
        string? error = null;
        try
        {
            rows = await sync.RunAsync(ct);
            logger.LogInformation("{Sync} finished", typeof(T).Name);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            logger.LogError(e, "{Sync} failed; retrying in {Interval}", typeof(T).Name, Interval);
        }

        // Recording the run must not stop the worker either, e.g. when Postgres itself is down.
        try
        {
            await using var insert = postgres.CreateCommand("""
                INSERT INTO ops.sync_runs (source, started_at, finished_at, ok, error, rows_written)
                VALUES (@source, @started, @finished, @ok, @error, @rows)
                """);
            insert.Parameters.AddWithValue("source", sync.Source);
            insert.Parameters.AddWithValue("started", startedAt);
            insert.Parameters.AddWithValue("finished", clock.GetUtcNow());
            insert.Parameters.AddWithValue("ok", error is null);
            insert.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
            insert.Parameters.AddWithValue("rows", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rows));
            await insert.ExecuteNonQueryAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "Couldn't record the {Sync} run in ops.sync_runs", typeof(T).Name);
        }
    }
}
