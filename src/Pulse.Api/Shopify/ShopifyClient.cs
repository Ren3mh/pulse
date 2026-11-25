using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pulse.Api.Shopify;

// Talks to the Shopify Admin GraphQL API. Gets its own access token with the
// client-credentials grant. Tokens last 24 h; a new one is fetched an hour
// before the old one runs out, so a long-running sync never uses a dead one.
public class ShopifyClient(HttpClient http, string storeDomain, string clientId, string clientSecret,
    TimeProvider clock, string apiVersion = "2026-10")
{
    public static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromHours(1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<JsonNode> QueryAsync(string query, object? variables = null, CancellationToken ct = default)
    {
        if (_token is null || clock.GetUtcNow() >= _expiresAt - RefreshBeforeExpiry)
            await RefreshTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{storeDomain}/admin/api/{apiVersion}/graphql.json")
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        request.Headers.Add("X-Shopify-Access-Token", _token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        if (body["errors"] is { } errors)
            throw new InvalidOperationException($"Shopify GraphQL error: {errors.ToJsonString()}");
        return body["data"]!;
    }

    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        var requestedAt = clock.GetUtcNow();
        using var response = await http.PostAsync($"https://{storeDomain}/admin/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Shopify token request failed ({(int)response.StatusCode}). Check Shopify:ClientId and Shopify:ClientSecret.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        _token = json.RootElement.GetProperty("access_token").GetString()!;
        var lifetime = json.RootElement.TryGetProperty("expires_in", out var seconds)
            ? TimeSpan.FromSeconds(seconds.GetInt32())
            : TimeSpan.FromHours(24);
        _expiresAt = requestedAt + lifetime;
    }
}
