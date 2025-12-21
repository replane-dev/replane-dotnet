using Replane;

Console.WriteLine("=== Replane Background Worker Example ===");
Console.WriteLine("This example demonstrates real-time config updates.\n");

// Create the client
await using var client = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = Environment.GetEnvironmentVariable("REPLANE_BASE_URL")
              ?? "https://your-replane-server.com",
    SdkKey = Environment.GetEnvironmentVariable("REPLANE_SDK_KEY")
             ?? "your-sdk-key",
    Fallbacks = new Dictionary<string, object?>
    {
        ["worker-enabled"] = true,
        ["batch-size"] = 100,
        ["interval-seconds"] = 30
    }
});

// Subscribe to config changes using ConfigChanged event
client.ConfigChanged += (sender, e) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Config changed: {e.ConfigName}");

    // Handle specific config changes
    if (e.ConfigName == "batch-size")
    {
        var newBatchSize = e.GetValue<int>();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch size updated to: {newBatchSize}");
        Console.WriteLine("  -> Worker will adjust batch processing...");
    }
};

try
{
    Console.WriteLine("Connecting to Replane...");
    await client.ConnectAsync();
    Console.WriteLine("Connected! Subscribed to config changes.\n");
}
catch (ReplaneException ex)
{
    Console.WriteLine($"Note: Running with fallbacks ({ex.Message})\n");
}

// Simulate a background worker
Console.WriteLine("Starting background worker...");
Console.WriteLine("Press Ctrl+C to stop.\n");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var iteration = 0;
while (!cts.Token.IsCancellationRequested)
{
    iteration++;

    // Read current config values
    var enabled = client.Get<bool>("worker-enabled");
    var batchSize = client.Get<int>("batch-size");
    var interval = client.Get<int>("interval-seconds");

    if (!enabled)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Worker is disabled, skipping...");
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Processing batch #{iteration} (size: {batchSize})");

        // Simulate work
        await Task.Delay(500, cts.Token);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Batch #{iteration} completed");
    }

    // Wait for next interval
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Waiting {interval} seconds...\n");

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

// Cleanup
Console.WriteLine("\nShutting down...");
Console.WriteLine("Done!");
