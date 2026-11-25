using Npgsql;

namespace Pulse.Api.Sync;

public static class Sql
{
    // Runs one of the .sql files embedded in Pulse.Api, e.g. "raw_ax.sql".
    public static async Task RunScriptAsync(this NpgsqlConnection connection, string name, CancellationToken ct)
    {
        var assembly = typeof(Sql).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(ct), connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
