using System.Text.Json.Nodes;

namespace Pulse.Seed;

// Creates or updates the catalogue in Shopify. productSet with the handle as
// identifier updates an existing product, so rerunning gives no duplicates.
public class ShopifySeeder(ShopifyClient shopify)
{
    public const string ColourOption = "Farve";

    private const string ProductSet = """
        mutation ($identifier: ProductSetIdentifiers, $input: ProductSetInput!) {
          productSet(identifier: $identifier, input: $input, synchronous: true) {
            product { id }
            userErrors { field message }
          }
        }
        """;

    public async Task SeedAsync(TextWriter log)
    {
        var locations = await shopify.QueryAsync("{ locations(first: 1) { nodes { id name } } }");
        var location = locations["locations"]!["nodes"]![0]!;
        var locationId = location["id"]!.GetValue<string>();
        log.WriteLine($"Shopify location: {location["name"]}");

        foreach (var product in Catalogue.Products)
        {
            var data = await shopify.QueryAsync(ProductSet, new
            {
                identifier = new { handle = product.Handle },
                input = Input(product, locationId),
            });

            var errors = data["productSet"]!["userErrors"]!.AsArray();
            if (errors.Count > 0)
                throw new InvalidOperationException($"{product.Handle}: {errors.ToJsonString()}");
            log.WriteLine($"  {product.Title} ({product.Variants.Length} variants)");
        }
    }

    public static object Input(Product product, string locationId) => new
    {
        handle = product.Handle,
        title = product.Title,
        vendor = product.Brand,
        productType = product.Category,
        // B2B-only products are kept out of the webshop.
        status = product.Variants.All(v => Catalogue.B2BOnly.Contains(v.Sku)) ? "DRAFT" : "ACTIVE",
        descriptionHtml = $"<p>{product.Title} i {product.Material.ToLowerInvariant()} fra {product.Brand}.</p>",
        productOptions = new[]
        {
            new { name = ColourOption, values = product.Variants.Select(v => new { name = v.Colour }).ToArray() },
        },
        metafields = new[]
        {
            new { @namespace = "custom", key = "material", type = "single_line_text_field", value = product.Material },
        },
        variants = product.Variants.Select(v => new
        {
            optionValues = new[] { new { optionName = ColourOption, name = v.Colour } },
            price = v.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
            barcode = v.Gtin,
            inventoryItem = new { sku = v.Sku, tracked = true },
            // The webshop sells from the warehouse, so Shopify shows warehouse stock.
            inventoryQuantities = new[] { new { locationId, name = "available", quantity = v.Stock } },
        }).ToArray(),
    };
}
