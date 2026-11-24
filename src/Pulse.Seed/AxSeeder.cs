using Microsoft.Data.SqlClient;

namespace Pulse.Seed;

// Fills the AX emulator: items, stock, prices, customers and order history.
// Clears the company first, so rerunning gives the same data, not duplicates.
public class AxSeeder(string connectionString, string dataAreaId)
{
    public const string Warehouse = "LAGER";
    public const string Showroom = "BUTIK";

    // Orders newer than this are still open (status 1), so their stock is reserved.
    private static readonly TimeSpan OpenFor = TimeSpan.FromDays(14);

    private static readonly string[] Tables =
        ["SALESLINE", "SALESTABLE", "CUSTTABLE", "PRICEDISCTABLE", "INVENTSUM", "INVENTDIM", "INVENTTABLE"];

    private long _recId;

    public async Task SeedAsync(DateTime today)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        async Task Insert(string table, Dictionary<string, object> row)
        {
            row["DATAAREAID"] = dataAreaId;
            row["RECID"] = ++_recId;
            var columns = string.Join(", ", row.Keys);
            var values = string.Join(", ", row.Keys.Select(k => "@" + k));
            await using var command = new SqlCommand($"INSERT INTO {table} ({columns}) VALUES ({values})", connection, transaction);
            foreach (var (key, value) in row) command.Parameters.AddWithValue("@" + key, value);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var table in Tables)
        {
            await using var delete = new SqlCommand($"DELETE FROM {table} WHERE DATAAREAID = @area", connection, transaction);
            delete.Parameters.AddWithValue("@area", dataAreaId);
            await delete.ExecuteNonQueryAsync();
        }
        _recId = await NextRecIdAsync(connection, transaction);

        foreach (var location in new[] { Warehouse, Showroom })
            await Insert("INVENTDIM", new() { ["INVENTDIMID"] = DimId(location), ["INVENTSITEID"] = "1", ["INVENTLOCATIONID"] = location });

        var orders = SalesHistory.Generate(today);
        var reserved = orders
            .Where(o => today - o.Created < OpenFor)
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.Sku)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

        foreach (var (product, variant) in Catalogue.Items)
        {
            await Insert("INVENTTABLE", new()
            {
                ["ITEMID"] = variant.Sku,
                ["ITEMNAME"] = $"{product.Title}, {variant.Colour}",
                ["ITEMGROUPID"] = product.Category,
            });

            var stock = new Dictionary<string, int> { [Warehouse] = variant.Stock };
            if (!Catalogue.B2BOnly.Contains(variant.Sku)) stock[Showroom] = 1; // one on display
            foreach (var (location, quantity) in stock)
            {
                var reservedHere = location == Warehouse ? Math.Min(reserved.GetValueOrDefault(variant.Sku), quantity) : 0;
                await Insert("INVENTSUM", new()
                {
                    ["ITEMID"] = variant.Sku,
                    ["INVENTDIMID"] = DimId(location),
                    ["PHYSICALINVENT"] = quantity,
                    ["RESERVPHYSICAL"] = reservedHere,
                    ["AVAILPHYSICAL"] = quantity - reservedHere,
                });
            }

            // List price for all customers: RELATION 4 (sales price), ITEMCODE 0 (one item), ACCOUNTCODE 2 (all).
            await Insert("PRICEDISCTABLE", Price(variant.Sku, 2, "", variant.Price));
        }

        foreach (var (account, prices) in SalesHistory.CustomerPrices)
            foreach (var (sku, price) in prices)
                await Insert("PRICEDISCTABLE", Price(sku, 0, account, price));

        foreach (var customer in SalesHistory.Customers)
            await Insert("CUSTTABLE", new()
            {
                ["ACCOUNTNUM"] = customer.AccountNum,
                ["NAME"] = customer.Name,
                ["CUSTGROUP"] = customer.Group,
                ["CREATEDDATETIME"] = today.AddMonths(-24),
            });

        foreach (var order in orders)
        {
            var status = today - order.Created < OpenFor ? 1 : 3; // 1 open, 3 invoiced
            await Insert("SALESTABLE", new()
            {
                ["SALESID"] = order.SalesId,
                ["CUSTACCOUNT"] = order.CustAccount,
                ["SALESSTATUS"] = status,
                ["CREATEDDATETIME"] = order.Created,
            });

            var location = order.CustAccount == SalesHistory.WalkIn ? Showroom : Warehouse;
            foreach (var (line, index) in order.Lines.Select((l, i) => (l, i)))
                await Insert("SALESLINE", new()
                {
                    ["SALESID"] = order.SalesId,
                    ["LINENUM"] = index + 1,
                    ["ITEMID"] = line.Sku,
                    ["INVENTDIMID"] = DimId(location),
                    ["SALESQTY"] = line.Qty,
                    ["SALESPRICE"] = line.Price,
                    ["LINEAMOUNT"] = line.Qty * line.Price,
                    ["SALESSTATUS"] = status,
                    ["CREATEDDATETIME"] = order.Created,
                });
        }

        await transaction.CommitAsync();
    }

    public static string DimId(string location) => $"DIM-{location}";

    private static Dictionary<string, object> Price(string sku, int accountCode, string account, decimal amount) => new()
    {
        ["RELATION"] = 4,
        ["ITEMCODE"] = 0,
        ["ITEMRELATION"] = sku,
        ["ACCOUNTCODE"] = accountCode,
        ["ACCOUNTRELATION"] = account,
        ["AMOUNT"] = amount,
    };

    // RECID is unique across companies in AX, so continue after other companies' rows.
    private static async Task<long> NextRecIdAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var max = string.Join(" UNION ALL ", Tables.Select(t => $"SELECT MAX(RECID) AS R FROM {t}"));
        await using var command = new SqlCommand($"SELECT ISNULL(MAX(R), 5637144576) FROM ({max}) x", connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
