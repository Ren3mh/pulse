using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pulse.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Sync:Enabled", "false")).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
