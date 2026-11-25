using Npgsql;
using Pulse.Api.Sync;

namespace Pulse.Api.Core;

public static class Schema
{
    // The raw tables first: the core views read them.
    private static readonly string[] Scripts = ["raw_ax.sql", "raw_shopify.sql", "core.sql"];

    // Creates the raw_* tables and recreates the core views. Run at startup.
    public static async Task CreateAsync(NpgsqlDataSource postgres, CancellationToken ct = default)
    {
        await using var connection = await postgres.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var script in Scripts) await connection.RunScriptAsync(script, ct);
        await transaction.CommitAsync(ct);
    }
}
