# Background Worker Example

Demonstrates using Replane in a long-running background service with real-time config updates.

## Prerequisites

- .NET 8.0 SDK or later
- A running Replane server
- An SDK key from your Replane project

## Setup

1. Copy this directory to your local machine:
   ```bash
   cp -r BackgroundWorker ~/my-replane-example
   cd ~/my-replane-example
   ```

2. Set your environment variables:
   ```bash
   export REPLANE_BASE_URL="https://your-replane-server.com"
   export REPLANE_SDK_KEY="your-sdk-key"
   ```

3. Restore packages:
   ```bash
   dotnet restore
   ```

## Running

```bash
dotnet run
```

Press `Ctrl+C` to stop the worker.

## What This Example Demonstrates

### Subscriptions

Subscribe to config changes and react in real-time:

```csharp
// Subscribe to ALL config changes
var unsubscribe = client.Subscribe((configName, config) =>
{
    Console.WriteLine($"Config changed: {configName} = {config.Value}");
});

// Subscribe to a SPECIFIC config
var unsubscribe = client.SubscribeConfig("batch-size", config =>
{
    Console.WriteLine($"Batch size is now: {config.Value}");
});
```

### Unsubscribing

Both `Subscribe()` and `SubscribeConfig()` return an unsubscribe function:

```csharp
var unsubscribe = client.Subscribe(...);

// Later, when you want to stop receiving updates:
unsubscribe();
```

### Real-Time Updates

The Replane client maintains a persistent SSE connection:
- Config changes are pushed from the server instantly
- No polling required
- Subscriptions fire immediately when configs change

### Dynamic Configuration

This example shows how to dynamically adjust worker behavior:
- `worker-enabled`: Toggle the worker on/off
- `batch-size`: Change processing batch size
- `interval-seconds`: Adjust the polling interval

## Sample Output

```
=== Replane Background Worker Example ===
This example demonstrates real-time config updates.

Connecting to Replane...
Connected! Subscribed to config changes.

Starting background worker...
Press Ctrl+C to stop.

[14:30:00] Processing batch #1 (size: 100)
[14:30:00] Batch #1 completed
[14:30:00] Waiting 30 seconds...

[14:30:15] Config changed: batch-size = 200
[14:30:15] Batch size updated to: 200
  -> Worker will adjust batch processing...

[14:30:30] Processing batch #2 (size: 200)
[14:30:30] Batch #2 completed
[14:30:30] Waiting 30 seconds...
```

## Use Cases

- **Background job processors**: Adjust batch sizes, intervals dynamically
- **Queue consumers**: Enable/disable consumption without restart
- **Scheduled tasks**: Change cron expressions at runtime
- **Feature flags**: Enable new features for background processes
- **Rate limiting**: Dynamically adjust rate limits

## Best Practices

1. **Always unsubscribe** when shutting down to prevent memory leaks
2. **Handle exceptions** in subscription callbacks
3. **Don't block** the subscription callback - offload heavy work
4. **Use specific subscriptions** when you only care about one config
