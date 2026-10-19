var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Lets Docker, CI and later the load balancer check that the API is up.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
