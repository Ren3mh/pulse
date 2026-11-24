using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Pulse.Api.Tests.Ax;

// Runs ax/schema.sql the way docker compose's ax-init does.
public static class AxSchema
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    // sqlcmd splits batches on GO; SqlClient doesn't, so split here.
    public static async Task<SqlConnection> ApplyAsync(string connectionString)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ax", "schema.sql"));
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        await using var reset = new SqlCommand("SET NOEXEC OFF", connection);
        await reset.ExecuteNonQueryAsync();
        return connection;
    }

    public static async Task<T> ScalarAsync<T>(this SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
