// Seeds the lab: the same furniture catalogue in the AX emulator and the
// Shopify dev store. Usage: dotnet run --project src/Pulse.Seed -- [ax|shopify]
// (no argument: both). Safe to rerun.
using Microsoft.Extensions.Configuration;
using Pulse.Seed;

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var target = args.FirstOrDefault() ?? "all";
var today = DateTime.UtcNow.Date;

if (target is "all" or "ax")
{
    Console.WriteLine("Seeding AX...");
    await new AxSeeder(config["Ax:ConnectionString"]!, config["Ax:DataAreaId"]!).SeedAsync(today);
    Console.WriteLine($"  {Catalogue.Items.Count()} items, {SalesHistory.Customers.Length} customers, {SalesHistory.Generate(today).Count} orders");
}

if (target is "all" or "shopify")
{
    Console.WriteLine("Seeding Shopify...");
    var shopify = new ShopifyClient(new HttpClient(),
        Required("Shopify:StoreDomain"), Required("Shopify:ClientId"), Required("Shopify:ClientSecret"));
    await new ShopifySeeder(shopify).SeedAsync(Console.Out);
}

string Required(string key) =>
    config[key] ?? throw new InvalidOperationException($"{key} is missing. Set it with dotnet user-secrets (see README).");
