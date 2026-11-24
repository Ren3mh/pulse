using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Pulse.Api.Tests.Ax;

// Runs ax/schema.sql against a real SQL Server, the same way docker compose does.
public class AxSchemaTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _sql.StartAsync();

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    [Fact]
    public async Task Schema_creates_the_ax_tables()
    {
        await using var connection = await RunSchemaAsync();

        var tables = await ListTablesAsync(connection);

        Assert.Equal(
            ["CUSTTABLE", "INVENTDIM", "INVENTSUM", "INVENTTABLE", "PRICEDISCTABLE", "SALESLINE", "SALESTABLE"],
            tables);
    }

    [Fact]
    public async Task Schema_is_safe_to_rerun()
    {
        await (await RunSchemaAsync()).DisposeAsync();

        await using var connection = await RunSchemaAsync();

        Assert.Equal(7, (await ListTablesAsync(connection)).Count);
    }

    // sqlcmd splits batches on GO; SqlClient doesn't, so split here.
    private async Task<SqlConnection> RunSchemaAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ax", "schema.sql"));
        var connection = new SqlConnection(_sql.GetConnectionString());
        await connection.OpenAsync();

        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<List<string>> ListTablesAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SET NOEXEC OFF; SELECT name FROM DynamicsAx.sys.tables ORDER BY name", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }
}
