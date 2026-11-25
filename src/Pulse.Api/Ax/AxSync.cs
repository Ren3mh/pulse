using Microsoft.Data.SqlClient;
using Npgsql;
using Pulse.Api.Sync;

namespace Pulse.Api.Ax;

// Copies one AX company's tables into raw_ax. Reads every row each run: AX
// has no reliable change tracking on these tables. All of it is written in
// one transaction, so readers never see a half-finished sync.
public class AxSync(string axConnectionString, string dataAreaId, NpgsqlDataSource postgres, TimeProvider clock) : ISync
{
    // AX table -> the columns Pulse copies. raw_ax has the same names in lower case.
    public static readonly Dictionary<string, string[]> Tables = new()
    {
        ["INVENTTABLE"] = ["ITEMID", "ITEMNAME", "ITEMGROUPID", "DATAAREAID", "MODIFIEDDATETIME", "RECID"],
        ["INVENTITEMBARCODE"] = ["ITEMBARCODE", "ITEMID", "INVENTDIMID", "BARCODESETUPID", "USEFORINPUT", "DATAAREAID", "RECID"],
        ["INVENTDIM"] = ["INVENTDIMID", "INVENTSITEID", "INVENTLOCATIONID", "INVENTCOLORID", "INVENTSIZEID", "CONFIGID", "DATAAREAID", "RECID"],
        ["PRICEDISCTABLE"] = ["RELATION", "ITEMCODE", "ITEMRELATION", "ACCOUNTCODE", "ACCOUNTRELATION", "QUANTITYAMOUNT", "AMOUNT", "CURRENCY", "FROMDATE", "TODATE", "DATAAREAID", "RECID"],
        ["CUSTTABLE"] = ["ACCOUNTNUM", "NAME", "CUSTGROUP", "CURRENCY", "DATAAREAID", "CREATEDDATETIME", "RECID"],
        ["SALESTABLE"] = ["SALESID", "CUSTACCOUNT", "SALESSTATUS", "SALESTYPE", "CURRENCYCODE", "DATAAREAID", "CREATEDDATETIME", "RECID"],
        ["SALESLINE"] = ["SALESID", "LINENUM", "ITEMID", "INVENTDIMID", "SALESQTY", "SALESPRICE", "LINEAMOUNT", "SALESSTATUS", "DATAAREAID", "CREATEDDATETIME", "RECID"],
    };

    private static readonly string[] StockColumns =
        ["ITEMID", "INVENTDIMID", "PHYSICALINVENT", "RESERVPHYSICAL", "AVAILPHYSICAL", "ONORDER", "DATAAREAID", "MODIFIEDDATETIME", "RECID"];

    public async Task RunAsync(CancellationToken ct)
    {
        var syncedAt = clock.GetUtcNow();

        await using var ax = new SqlConnection(axConnectionString);
        await ax.OpenAsync(ct);
        await using var pg = await postgres.OpenConnectionAsync(ct);
        await pg.RunScriptAsync("raw_ax.sql", ct);
        await using var transaction = await pg.BeginTransactionAsync(ct);

        foreach (var (table, columns) in Tables)
        {
            var target = "raw_ax." + table.ToLowerInvariant();
            await using (var delete = new NpgsqlCommand($"DELETE FROM {target} WHERE dataareaid = @area", pg, transaction))
            {
                delete.Parameters.AddWithValue("area", dataAreaId);
                await delete.ExecuteNonQueryAsync(ct);
            }
            await CopyAsync(ax, pg, table, target, columns, syncedAt, ct);
        }

        await CopyAsync(ax, pg, "INVENTSUM", "raw_ax.stock_snapshots", StockColumns, syncedAt, ct);

        await transaction.CommitAsync(ct);
    }

    private async Task CopyAsync(SqlConnection ax, NpgsqlConnection pg, string source, string target,
        string[] columns, DateTimeOffset syncedAt, CancellationToken ct)
    {
        await using var select = new SqlCommand($"SELECT {string.Join(", ", columns)} FROM {source} WHERE DATAAREAID = @area", ax);
        select.Parameters.AddWithValue("@area", dataAreaId);
        await using var reader = await select.ExecuteReaderAsync(ct);

        var targetColumns = string.Join(", ", columns.Select(c => c.ToLowerInvariant()));
        await using var import = await pg.BeginBinaryImportAsync($"COPY {target} ({targetColumns}, synced_at) FROM STDIN (FORMAT BINARY)", ct);
        while (await reader.ReadAsync(ct))
        {
            await import.StartRowAsync(ct);
            for (var i = 0; i < columns.Length; i++)
            {
                var value = reader.GetValue(i);
                // AX stores DATETIME in UTC without saying so.
                if (value is DateTime dateTime) value = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                await import.WriteAsync(value, ct);
            }
            await import.WriteAsync(syncedAt, ct);
        }
        await import.CompleteAsync(ct);
    }
}
