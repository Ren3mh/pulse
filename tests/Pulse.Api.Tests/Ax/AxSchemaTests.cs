using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Pulse.Api.Tests.Ax;

public class AxSchemaTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(AxSchema.Image).Build();

    public Task InitializeAsync() => _sql.StartAsync();

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    [Fact]
    public async Task Schema_creates_the_ax_tables()
    {
        await using var connection = await AxSchema.ApplyAsync(_sql.GetConnectionString());

        var tables = await ListTablesAsync(connection);

        Assert.Equal(
            ["CUSTTABLE", "INVENTDIM", "INVENTITEMBARCODE", "INVENTSUM", "INVENTTABLE", "PRICEDISCTABLE", "SALESLINE", "SALESTABLE"],
            tables);
    }

    [Fact]
    public async Task Schema_is_safe_to_rerun()
    {
        await (await AxSchema.ApplyAsync(_sql.GetConnectionString())).DisposeAsync();

        await using var connection = await AxSchema.ApplyAsync(_sql.GetConnectionString());

        Assert.Equal(8, (await ListTablesAsync(connection)).Count);
    }

    private static async Task<List<string>> ListTablesAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT name FROM DynamicsAx.sys.tables ORDER BY name", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }
}
