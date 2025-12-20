# Replane .NET SDK Examples

This directory contains example projects demonstrating various uses of the Replane .NET SDK.

## Examples

| Example | Description |
|---------|-------------|
| [BasicUsage](./BasicUsage/) | Simple console app with basic config reading |
| [ConsoleWithOverrides](./ConsoleWithOverrides/) | Context-based overrides and user segmentation |
| [BackgroundWorker](./BackgroundWorker/) | Long-running service with real-time config updates |
| [WebApiIntegration](./WebApiIntegration/) | ASP.NET Core Web API with middleware and DI |
| [UnitTesting](./UnitTesting/) | Unit testing with the in-memory test client |

## Quick Start

Each example is a self-contained project. To run any example:

1. **Copy the example directory** to your local machine:
   ```bash
   cp -r <example-name> ~/my-example
   cd ~/my-example
   ```

2. **Set environment variables** (or edit the code):
   ```bash
   export REPLANE_BASE_URL="https://your-replane-server.com"
   export REPLANE_SDK_KEY="your-sdk-key"
   ```

3. **Restore and run**:
   ```bash
   dotnet restore
   dotnet run
   ```

## Example Overview

### BasicUsage

The simplest possible example - connect and read configs:

```csharp
var client = new ReplaneClient(options);
await client.ConnectAsync();
var value = client.Get<bool>("feature-enabled");
```

### ConsoleWithOverrides

Shows how to use context for user-specific configuration:

```csharp
var context = new ReplaneContext
{
    ["user_id"] = "user-123",
    ["plan"] = "premium"
};
var feature = client.Get<bool>("premium-feature", context);
```

### BackgroundWorker

Demonstrates subscriptions for real-time updates:

```csharp
client.Subscribe((name, config) =>
{
    Console.WriteLine($"Config {name} changed!");
});
```

### WebApiIntegration

Full ASP.NET Core integration with middleware:

```csharp
// Maintenance mode middleware
app.Use(async (context, next) =>
{
    if (client.Get<bool>("maintenance-mode"))
    {
        context.Response.StatusCode = 503;
        return;
    }
    await next();
});
```

### UnitTesting

Test your code without a server:

```csharp
using var config = TestClient.Create(new Dictionary<string, object?>
{
    ["feature-enabled"] = true
});
var service = new MyService(config);
// Test with mocked configs
```

## Requirements

- .NET 8.0 SDK or later
- A Replane server (except for UnitTesting example)

## Notes

- All examples use fallback values, so they'll work even without a server
- Each example has its own README with detailed instructions
- Examples reference the `Replane` NuGet package (version 0.1.0)
