using Npgsql;
using Pulse.Api.Ax;
using Pulse.Api.Shopify;
using Pulse.Api.Sync;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Copies AX and Shopify into Postgres every 15 minutes. Off in tests that don't need it.
var syncEnabled = config.GetValue("Sync:Enabled", true);
var shopifyConfigured = !string.IsNullOrEmpty(config["Shopify:ClientSecret"]);
if (syncEnabled)
{
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(NpgsqlDataSource.Create(config.GetConnectionString("Postgres")!));

    builder.Services.AddSingleton(sp => new AxSync(
        config["Ax:ConnectionString"]!, config["Ax:DataAreaId"]!, sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddHostedService<SyncWorker<AxSync>>();

    if (shopifyConfigured)
    {
        builder.Services.AddSingleton(sp => new ShopifyClient(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }),
            config["Shopify:StoreDomain"]!, config["Shopify:ClientId"]!, config["Shopify:ClientSecret"]!,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ShopifySync>();
        builder.Services.AddHostedService<SyncWorker<ShopifySync>>();
    }
}

var app = builder.Build();

if (syncEnabled && !shopifyConfigured)
    app.Logger.LogWarning("Shopify sync is off: Shopify credentials are missing. Set them with dotnet user-secrets (see README).");

// Lets Docker, CI and later the load balancer check that the API is up.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
