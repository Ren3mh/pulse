using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pulse.Api.Tests.Core;

namespace Pulse.Api.Tests.Dashboard;

public class DashboardEndpointTests(LabFixture lab) : IClassFixture<LabFixture>
{
    private HttpClient Client() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("Sync:Enabled", "false")
        .UseSetting("ConnectionStrings:Postgres", lab.ConnectionString)).CreateClient();

    [Fact]
    public async Task Serves_the_page()
    {
        var response = await Client().GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("/dashboard/data", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Serves_the_data()
    {
        var json = JsonDocument.Parse(await Client().GetStringAsync("/dashboard/data?range=24h&sku=OSLO3-NAT")).RootElement;

        Assert.Equal(["ax", "shopify"], json.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("OSLO3-NAT", json.GetProperty("series").GetProperty("sku").GetString());
        Assert.Equal("LAGER", json.GetProperty("stock").GetProperty("warehouse").GetString());
    }

    [Fact]
    public async Task Rejects_an_unknown_range()
    {
        var response = await Client().GetAsync("/dashboard/data?range=1y");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
