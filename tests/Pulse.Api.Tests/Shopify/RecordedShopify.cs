using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Pulse.Api.Shopify;

namespace Pulse.Api.Tests.Shopify;

// Answers like the Shopify dev store, from the files in Recordings/.
// products.json was recorded from the store; the store has no orders (the app
// lacks write_orders), so the order pages are hand-written in the same shape.
public class RecordedShopify : HttpMessageHandler
{
    private const string Token = "test-token";

    public int TokenRequests { get; private set; }

    public ShopifyClient Client(TimeProvider clock) =>
        new(new HttpClient(this), "pulsejacobsen.myshopify.com", "client-id", "shpss_secret", clock);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath == "/admin/oauth/access_token")
        {
            TokenRequests++;
            return Json($$"""{"access_token":"{{Token}}","scope":"read_orders,write_products","expires_in":86399}""");
        }

        if (!request.Headers.TryGetValues("X-Shopify-Access-Token", out var tokens) || tokens.Single() != Token)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        var query = body["query"]!.GetValue<string>();
        var after = body["variables"]?["after"]?.GetValue<string>();
        var file = query.Contains("products(") ? "products.json"
            : after is null ? "orders-page-1.handwritten.json"
            : "orders-page-2.handwritten.json";
        return Json(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Shopify", "Recordings", file), ct));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
