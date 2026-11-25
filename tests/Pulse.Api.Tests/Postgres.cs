using Npgsql;

namespace Pulse.Api.Tests;

public static class Postgres
{
    // Same as compose.yaml.
    public const string Image = "postgres:18";

    public static async Task<T> ScalarAsync<T>(this NpgsqlDataSource db, string sql)
    {
        await using var command = db.CreateCommand(sql);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    public static async Task<Dictionary<string, T>> ByKeyAsync<T>(this NpgsqlDataSource db, string sql)
    {
        await using var command = db.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, T>();
        while (await reader.ReadAsync()) rows[reader.GetString(0)] = (T)Convert.ChangeType(reader.GetValue(1), typeof(T));
        return rows;
    }
}
