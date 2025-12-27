using Replane;

Console.WriteLine("=== Replane Context & Overrides Example ===\n");

// Create client with a default context
// This context is merged with per-request context
await using var client = new ReplaneClient(new ReplaneClientOptions
{
    // Default context - applied to all config reads
    Context = new ReplaneContext
    {
        ["app_version"] = "2.0.0",
        ["platform"] = "console"
    },

    // Defaults for demo purposes
    Defaults = new Dictionary<string, object?>
    {
        ["premium-feature"] = false,
        ["rate-limit"] = 100,
        ["welcome-message"] = "Hello!"
    }
});

try
{
    await client.ConnectAsync(new ConnectOptions
    {
        BaseUrl = Environment.GetEnvironmentVariable("REPLANE_BASE_URL")
                  ?? "https://your-replane-server.com",
        SdkKey = Environment.GetEnvironmentVariable("REPLANE_SDK_KEY")
                 ?? "your-sdk-key"
    });
    Console.WriteLine("Connected to Replane server.\n");
}
catch (ReplaneException ex)
{
    Console.WriteLine($"Note: Running with defaults only ({ex.Message})\n");
}

// Simulate different users
var users = new[]
{
    new { Id = "user-001", Plan = "free", Region = "us-east" },
    new { Id = "user-002", Plan = "premium", Region = "us-west" },
    new { Id = "user-003", Plan = "enterprise", Region = "eu-west" },
};

Console.WriteLine("Evaluating configs for different users:\n");
Console.WriteLine(new string('-', 60));

foreach (var user in users)
{
    // Create user-specific context
    var userContext = new ReplaneContext
    {
        ["user_id"] = user.Id,
        ["plan"] = user.Plan,
        ["region"] = user.Region
    };

    // Get configs with user context
    // The user context is merged with the default context from options
    var premiumFeature = client.Get<bool>("premium-feature", userContext);
    var rateLimit = client.Get<int>("rate-limit", userContext);
    var welcomeMessage = client.Get<string>("welcome-message", userContext);

    Console.WriteLine($"User: {user.Id} (Plan: {user.Plan}, Region: {user.Region})");
    Console.WriteLine($"  premium-feature: {premiumFeature}");
    Console.WriteLine($"  rate-limit: {rateLimit}");
    Console.WriteLine($"  welcome-message: {welcomeMessage}");
    Console.WriteLine();
}

Console.WriteLine(new string('-', 60));
Console.WriteLine("\nContext Merging Demo:");
Console.WriteLine("Default context: app_version=2.0.0, platform=console");
Console.WriteLine("When you call Get() with additional context, it's merged.");
Console.WriteLine("Per-call context overrides default context for same keys.");

// Example of context merging
var partialContext = new ReplaneContext
{
    ["user_id"] = "demo-user"
    // platform will come from default context
};

Console.WriteLine($"\nWith partial context (user_id=demo-user):");
Console.WriteLine($"  The merged context has: user_id, app_version, platform");
