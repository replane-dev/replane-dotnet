using Replane;

var builder = WebApplication.CreateBuilder(args);

// Create and configure the Replane client as a singleton
var replaneClient = new ReplaneClient(new ReplaneClientOptions
{
    Defaults = new Dictionary<string, object?>
    {
        ["api-rate-limit"] = 100,
        ["premium-feature-enabled"] = false,
        ["maintenance-mode"] = false,
        ["welcome-message"] = "Welcome to the API!"
    }
});

// Register the client as the interface for easy testing/mocking
builder.Services.AddSingleton<IReplaneClient>(replaneClient);

var app = builder.Build();

// Connect to Replane during startup
try
{
    await replaneClient.ConnectAsync(new ConnectOptions
    {
        BaseUrl = builder.Configuration["Replane:BaseUrl"]
                  ?? Environment.GetEnvironmentVariable("REPLANE_BASE_URL")
                  ?? "https://your-replane-server.com",
        SdkKey = builder.Configuration["Replane:SdkKey"]
                 ?? Environment.GetEnvironmentVariable("REPLANE_SDK_KEY")
                 ?? "your-sdk-key"
    });
    app.Logger.LogInformation("Connected to Replane server");
}
catch (ReplaneException ex)
{
    app.Logger.LogWarning("Running with default configs: {Message}", ex.Message);
}

// Middleware: Check maintenance mode
app.Use(async (context, next) =>
{
    var client = context.RequestServices.GetRequiredService<IReplaneClient>();
    var maintenanceMode = client.Get<bool>("maintenance-mode");

    if (maintenanceMode && !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = "Service is under maintenance" });
        return;
    }

    await next();
});

// Endpoints
app.MapGet("/", (IReplaneClient client) =>
{
    var message = client.Get<string>("welcome-message");
    return new { message };
});

app.MapGet("/health", () => new { status = "healthy" });

app.MapGet("/config/{name}", (string name, IReplaneClient client) =>
{
    try
    {
        var value = client.Get<object>(name);
        return Results.Ok(new { name, value });
    }
    catch (ConfigNotFoundException)
    {
        return Results.NotFound(new { error = $"Config '{name}' not found" });
    }
});

app.MapGet("/features", (HttpContext http, IReplaneClient client) =>
{
    // Extract user context from request (e.g., from headers or JWT)
    var userId = http.Request.Headers["X-User-Id"].FirstOrDefault() ?? "anonymous";
    var userPlan = http.Request.Headers["X-User-Plan"].FirstOrDefault() ?? "free";

    var context = new ReplaneContext
    {
        ["user_id"] = userId,
        ["plan"] = userPlan
    };

    return new
    {
        userId,
        plan = userPlan,
        features = new
        {
            premiumFeatureEnabled = client.Get<bool>("premium-feature-enabled", context),
            rateLimit = client.Get<int>("api-rate-limit", context),
        }
    };
});

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("Disposing Replane client...");
    replaneClient.Dispose();
});

app.Run();
