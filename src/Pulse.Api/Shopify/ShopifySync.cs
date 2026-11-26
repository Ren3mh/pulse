using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;
using Pulse.Api.Sync;

namespace Pulse.Api.Shopify;

// Copies products, variants, stock and orders from Shopify into raw_shopify.
// Everything is read first and then written in one transaction, so a failed
// request leaves the last good sync in place.
public class ShopifySync(ShopifyClient shopify, NpgsqlDataSource postgres, TimeProvider clock) : ISync
{
    // Nested page sizes keep the query under Shopify's 1000-point cost limit.
    public const string ProductsQuery = """
        query ($after: String) {
          products(first: 50, after: $after) {
            pageInfo { hasNextPage endCursor }
            nodes {
              id handle title vendor productType status updatedAt
              variants(first: 20) {
                pageInfo { hasNextPage }
                nodes {
                  id sku barcode price title updatedAt
                  inventoryItem {
                    id
                    inventoryLevels(first: 5) {
                      pageInfo { hasNextPage }
                      nodes { location { id name } quantities(names: ["available", "on_hand", "committed"]) { name quantity } }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public const string OrdersQuery = """
        query ($after: String) {
          orders(first: 50, after: $after, sortKey: CREATED_AT) {
            pageInfo { hasNextPage endCursor }
            nodes {
              id name createdAt updatedAt cancelledAt displayFinancialStatus displayFulfillmentStatus currencyCode
              customer { id }
              totalPriceSet { shopMoney { amount } }
              lineItems(first: 50) {
                pageInfo { hasNextPage }
                nodes { id sku quantity variant { id } originalUnitPriceSet { shopMoney { amount } } }
              }
            }
          }
        }
        """;

    public string Source => "shopify";

    public async Task<IReadOnlyDictionary<string, int>> RunAsync(CancellationToken ct)
    {
        var rows = new Dictionary<string, int>();
        var syncedAt = clock.GetUtcNow();
        var products = await ReadAllAsync(ProductsQuery, "products", ct);
        var orders = await ReadAllAsync(OrdersQuery, "orders", ct);

        await using var pg = await postgres.OpenConnectionAsync(ct);
        await pg.RunScriptAsync("raw_shopify.sql", ct);
        await using var transaction = await pg.BeginTransactionAsync(ct);

        async Task Write(string table, Dictionary<string, object?> row, string? upsertOn = null)
        {
            row["synced_at"] = syncedAt;
            var columns = string.Join(", ", row.Keys);
            var values = string.Join(", ", row.Keys.Select(k => "@" + k));
            var sql = $"INSERT INTO raw_shopify.{table} ({columns}) VALUES ({values})";
            if (upsertOn is not null)
                sql += $" ON CONFLICT ({upsertOn}) DO UPDATE SET " +
                       string.Join(", ", row.Keys.Where(k => k != upsertOn).Select(k => $"{k} = EXCLUDED.{k}"));
            await using var command = new NpgsqlCommand(sql, pg, transaction);
            foreach (var (key, value) in row) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
            rows["raw_shopify." + table] = rows.GetValueOrDefault("raw_shopify." + table) + 1;
        }

        await Execute(pg, transaction, "DELETE FROM raw_shopify.variants; DELETE FROM raw_shopify.products;", ct);

        foreach (var product in products)
        {
            await Write("products", new()
            {
                ["id"] = Text(product["id"]),
                ["handle"] = Text(product["handle"]),
                ["title"] = Text(product["title"]),
                ["vendor"] = Text(product["vendor"]),
                ["product_type"] = Text(product["productType"]),
                ["status"] = Text(product["status"]),
                ["updated_at"] = Time(product["updatedAt"]),
            });

            foreach (var variant in Nodes(product["variants"]!, $"variants of {Text(product["handle"])}"))
            {
                var item = variant["inventoryItem"]!;
                await Write("variants", new()
                {
                    ["id"] = Text(variant["id"]),
                    ["product_id"] = Text(product["id"]),
                    ["sku"] = TextOrNull(variant["sku"]),
                    ["barcode"] = TextOrNull(variant["barcode"]),
                    ["title"] = Text(variant["title"]),
                    ["price"] = Money(variant["price"]),
                    ["inventory_item_id"] = Text(item["id"]),
                    ["updated_at"] = Time(variant["updatedAt"]),
                });

                foreach (var level in Nodes(item["inventoryLevels"]!, $"stock locations of {Text(variant["sku"])}"))
                {
                    var quantities = level["quantities"]!.AsArray()
                        .ToDictionary(q => Text(q!["name"]), q => q!["quantity"]!.GetValue<int>());
                    await Write("stock_snapshots", new()
                    {
                        ["inventory_item_id"] = Text(item["id"]),
                        ["sku"] = TextOrNull(variant["sku"]),
                        ["location_id"] = Text(level["location"]!["id"]),
                        ["location_name"] = Text(level["location"]!["name"]),
                        ["available"] = quantities["available"],
                        ["on_hand"] = quantities["on_hand"],
                        ["committed"] = quantities["committed"],
                    });
                }
            }
        }

        foreach (var order in orders)
        {
            var orderId = Text(order["id"]);
            await Write("orders", new()
            {
                ["id"] = orderId,
                ["name"] = Text(order["name"]),
                ["customer_id"] = TextOrNull(order["customer"]?["id"]),
                ["currency_code"] = Text(order["currencyCode"]),
                ["total_price"] = Money(order["totalPriceSet"]!["shopMoney"]!["amount"]),
                ["financial_status"] = TextOrNull(order["displayFinancialStatus"]),
                ["fulfillment_status"] = TextOrNull(order["displayFulfillmentStatus"]),
                ["created_at"] = Time(order["createdAt"]),
                ["updated_at"] = Time(order["updatedAt"]),
                ["cancelled_at"] = order["cancelledAt"] is null ? null : Time(order["cancelledAt"]),
            }, upsertOn: "id");

            // An edited order can lose lines, so replace them rather than upsert.
            await using (var delete = new NpgsqlCommand("DELETE FROM raw_shopify.order_lines WHERE order_id = @id", pg, transaction))
            {
                delete.Parameters.AddWithValue("id", orderId);
                await delete.ExecuteNonQueryAsync(ct);
            }
            foreach (var line in Nodes(order["lineItems"]!, $"lines of order {Text(order["name"])}"))
                await Write("order_lines", new()
                {
                    ["id"] = Text(line["id"]),
                    ["order_id"] = orderId,
                    ["sku"] = TextOrNull(line["sku"]),
                    ["variant_id"] = TextOrNull(line["variant"]?["id"]),
                    ["quantity"] = line["quantity"]!.GetValue<int>(),
                    ["unit_price"] = Money(line["originalUnitPriceSet"]!["shopMoney"]!["amount"]),
                });
        }

        await transaction.CommitAsync(ct);
        return rows;
    }

    // Follows the cursor until the last page.
    private async Task<List<JsonNode>> ReadAllAsync(string query, string field, CancellationToken ct)
    {
        var nodes = new List<JsonNode>();
        string? after = null;
        do
        {
            var page = (await shopify.QueryAsync(query, new { after }, ct))[field]!;
            nodes.AddRange(page["nodes"]!.AsArray().Select(n => n!));
            after = page["pageInfo"]!["hasNextPage"]!.GetValue<bool>() ? Text(page["pageInfo"]!["endCursor"]) : null;
        } while (after is not null);
        return nodes;
    }

    // Nested lists are read in one page. Fail rather than silently drop the rest.
    private static IEnumerable<JsonNode> Nodes(JsonNode connection, string what)
    {
        if (connection["pageInfo"]!["hasNextPage"]!.GetValue<bool>())
            throw new InvalidOperationException($"Too many {what} for one page; the sync would miss some.");
        return connection["nodes"]!.AsArray().Select(n => n!);
    }

    private static async Task Execute(NpgsqlConnection pg, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, pg, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static string? TextOrNull(JsonNode? node) => node?.GetValue<string>() is { Length: > 0 } s ? s : null;

    private static decimal Money(JsonNode? node) => decimal.Parse(Text(node), CultureInfo.InvariantCulture);

    private static DateTimeOffset Time(JsonNode? node) => DateTimeOffset.Parse(Text(node), CultureInfo.InvariantCulture).ToUniversalTime();
}
