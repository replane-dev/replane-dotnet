using Replane;

// Configure the Replane client with defaults
var client = new ReplaneClient(new ReplaneClientOptions
{
    // Optional: Default values if server is unavailable
    Defaults = new Dictionary<string, object?>
    {
        ["feature-enabled"] = false,
        ["max-items"] = 10
    }
});

try
{
    // Connect to the Replane server with connection options
    Console.WriteLine("Connecting to Replane server...");
    await client.ConnectAsync(new ConnectOptions
    {
        // Replace with your Replane server URL
        BaseUrl = Environment.GetEnvironmentVariable("REPLANE_BASE_URL")
                  ?? "https://your-replane-server.com",

        // Replace with your SDK key
        SdkKey = Environment.GetEnvironmentVariable("REPLANE_SDK_KEY")
                 ?? "your-sdk-key"
    });
    Console.WriteLine("Connected successfully!");

    // Read some config values
    var featureEnabled = client.Get<bool>("feature-enabled");
    Console.WriteLine($"feature-enabled: {featureEnabled}");

    var maxItems = client.Get<int>("max-items");
    Console.WriteLine($"max-items: {maxItems}");

    // Get with a default value (no exception if not found)
    var timeout = client.Get<int>("timeout-ms", defaultValue: 5000);
    Console.WriteLine($"timeout-ms: {timeout}");

    // Try to get a non-existent config
    try
    {
        var missing = client.Get<string>("non-existent-config");
    }
    catch (ConfigNotFoundException ex)
    {
        Console.WriteLine($"Expected error: {ex.Message}");
    }
}
catch (AuthenticationException)
{
    Console.WriteLine("Error: Invalid SDK key. Please check your credentials.");
}
catch (ReplaneTimeoutException ex)
{
    Console.WriteLine($"Error: Connection timed out after {ex.TimeoutMs}ms");
}
catch (ReplaneException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    // Always dispose the client
    await client.DisposeAsync();
}
