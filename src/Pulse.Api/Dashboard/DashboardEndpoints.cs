using Npgsql;

namespace Pulse.Api.Dashboard;

public static class DashboardEndpoints
{
    // /dashboard is the page (wwwroot/dashboard.html); /dashboard/data is its
    // only data source, polled every 30 seconds. Internal to the page.
    public static void MapDashboard(this WebApplication app, DashboardData.Options options)
    {
        app.MapGet("/dashboard", () => Results.File(
            Path.Combine(app.Environment.WebRootPath, "dashboard.html"), "text/html; charset=utf-8"));

        app.MapGet("/dashboard/data", async (NpgsqlDataSource postgres, TimeProvider clock,
            string? range, string? sku, CancellationToken ct) =>
        {
            if (!DashboardData.Ranges.TryGetValue(range ?? "7d", out var span))
                return Results.BadRequest(new { error = $"range must be one of {string.Join(", ", DashboardData.Ranges.Keys)}" });
            await using var db = await postgres.OpenConnectionAsync(ct);
            return Results.Ok(await DashboardData.LoadAsync(db, options, clock.GetUtcNow(), span,
                string.IsNullOrWhiteSpace(sku) ? null : sku, ct));
        });
    }
}
