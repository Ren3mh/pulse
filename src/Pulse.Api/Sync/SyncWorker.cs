namespace Pulse.Api.Sync;

// One source copied into Postgres. Each run reads everything it needs again.
public interface ISync
{
    Task RunAsync(CancellationToken ct);
}

// Runs a sync at startup and then every 15 minutes. A failed run is logged
// and retried on the next tick, so one bad run doesn't stop the worker.
public class SyncWorker<T>(T sync, ILogger<SyncWorker<T>> logger) : BackgroundService where T : ISync
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await sync.RunAsync(ct);
                logger.LogInformation("{Sync} finished", typeof(T).Name);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogError(e, "{Sync} failed; retrying in {Interval}", typeof(T).Name, Interval);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
