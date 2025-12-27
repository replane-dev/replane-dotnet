using Replane;

// Configuration - update these values for your Replane server
var baseUrl = Environment.GetEnvironmentVariable("REPLANE_BASE_URL") ?? "http://localhost:3000";
var sdkKey = Environment.GetEnvironmentVariable("REPLANE_SDK_KEY") ?? "your-sdk-key";

Console.WriteLine("Replane .NET SDK Playground");
Console.WriteLine("===========================");
Console.WriteLine($"Base URL: {baseUrl}");
Console.WriteLine();

var clientOptions = new ReplaneClientOptions
{
    Debug = true,
    // Defaults for when server is unavailable
    Defaults = new Dictionary<string, object?>
    {
        ["feature-flag"] = false,
        ["max-items"] = 10
    }
};

var connectOptions = new ConnectOptions
{
    BaseUrl = baseUrl,
    SdkKey = sdkKey,
    ConnectionTimeoutMs = 1000
};

await using var client = new ReplaneClient(clientOptions);

// Subscribe to config changes
client.ConfigChanged += (sender, e) =>
{
    Console.WriteLine();
    Console.WriteLine($"[EVENT] Config changed: {e.ConfigName} = {e.Config.Value}");
    Console.WriteLine();
};

try
{
    Console.WriteLine("Connecting to Replane server...");
    await client.ConnectAsync(connectOptions);
    Console.WriteLine("Connected!");
    Console.WriteLine();

    // Try to get some config values
    Console.WriteLine("--- Getting config values ---");

    var featureFlag = client.Get<bool>("feature-flag", defaultValue: false);
    Console.WriteLine($"feature-flag = {featureFlag}");

    var maxItems = client.Get<int>("max-items", defaultValue: 100);
    Console.WriteLine($"max-items = {maxItems}");

    // Try with context
    Console.WriteLine();
    Console.WriteLine("--- Getting config with context ---");

    var context = new ReplaneContext
    {
        ["user_id"] = "user-123",
        ["plan"] = "premium"
    };

    var premiumFeature = client.Get<bool>("premium-feature", context: context, defaultValue: false);
    Console.WriteLine($"premium-feature (for premium user) = {premiumFeature}");

    // Try getting a complex config
    Console.WriteLine();
    Console.WriteLine("--- Getting complex config ---");

    var greetingConfig = client.Get<GreetingConfig>("hello", defaultValue: new GreetingConfig { Greeting = "Hello, world!" });
    Console.WriteLine($"greeting = {greetingConfig?.Greeting ?? "<null>"}");

    // Keep running to receive config updates
    Console.WriteLine();
    Console.WriteLine("Listening for config changes... Press Ctrl+C to exit.");

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Shutting down...");
    }
}
catch (AuthenticationException)
{
    Console.WriteLine("ERROR: Authentication failed. Check your SDK key.");
}
catch (ReplaneTimeoutException ex)
{
    Console.WriteLine($"ERROR: Connection timed out after {ex.TimeoutMs}ms");
    Console.WriteLine("Using default values instead.");

    var featureFlag = client.Get<bool>("feature-flag", defaultValue: false);
    Console.WriteLine($"feature-flag (default) = {featureFlag}");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
}

class GreetingConfig
{
    public string? Greeting { get; set; }
}