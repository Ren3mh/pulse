using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pulse.Seed;

// Talks to the Shopify Admin GraphQL API. Gets its own access token with the
// client-credentials grant; tokens last 24 h, so one per run is enough.
public class ShopifyClient(HttpClient http, string storeDomain, string clientId, string clientSecret, string apiVersion = "2026-10")
{
    private string? _token;

    public async Task<JsonNode> QueryAsync(string query, object? variables = null)
    {
        _token ??= await GetTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{storeDomain}/admin/api/{apiVersion}/graphql.json")
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        request.Headers.Add("X-Shopify-Access-Token", _token);

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        if (body["errors"] is { } errors)
            throw new InvalidOperationException($"Shopify GraphQL error: {errors.ToJsonString()}");
        return body["data"]!;
    }

    private async Task<string> GetTokenAsync()
    {
        using var response = await http.PostAsync($"https://{storeDomain}/admin/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Shopify token request failed ({(int)response.StatusCode}). Check Shopify:ClientId and Shopify:ClientSecret.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }
}
