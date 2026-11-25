using Microsoft.Extensions.Time.Testing;

namespace Pulse.Api.Tests.Shopify;

public class ShopifyClientTests
{
    private const string Query = "{ products(first: 1) { nodes { id } } }";

    [Fact]
    public async Task Reuses_the_token_until_an_hour_before_it_expires()
    {
        var clock = new FakeTimeProvider();
        var shopify = new RecordedShopify();
        var client = shopify.Client(clock);

        await client.QueryAsync(Query);
        clock.Advance(TimeSpan.FromHours(22));
        await client.QueryAsync(Query);
        Assert.Equal(1, shopify.TokenRequests);

        clock.Advance(TimeSpan.FromHours(1));
        await client.QueryAsync(Query);
        Assert.Equal(2, shopify.TokenRequests);
    }
}
