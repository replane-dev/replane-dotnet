# Replane .NET SDK

Official .NET SDK for [Replane](https://replane.dev) - Feature flags and remote configuration.

## Installation

```bash
dotnet add package Replane
```

## Quick Start

```csharp
using Replane;

// Create and connect the client
await using var client = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-replane-server.com",
    SdkKey = "your-sdk-key"
});

await client.ConnectAsync();

// Get a config value
var featureEnabled = client.Get<bool>("feature-enabled");
var maxItems = client.Get<int>("max-items", defaultValue: 100);
```

## Features

- **Real-time updates** via Server-Sent Events (SSE)
- **Client-side evaluation** - context never leaves your application
- **Gradual rollouts** with percentage-based segmentation
- **Override rules** with flexible conditions
- **Type-safe** configuration access
- **Async/await** support throughout
- **In-memory test client** for unit testing

## Usage

### Basic Configuration Access

```csharp
// Get typed config values
var enabled = client.Get<bool>("feature-enabled");
var limit = client.Get<int>("rate-limit");
var apiKey = client.Get<string>("api-key");

// With default values
var timeout = client.Get<int>("timeout-ms", defaultValue: 5000);
```

### Context-Based Overrides

Evaluate configs based on user context:

```csharp
// Create context for override evaluation
var context = new ReplaneContext
{
    ["user_id"] = "user-123",
    ["plan"] = "premium",
    ["region"] = "us-east"
};

// Get config with context
var premiumFeature = client.Get<bool>("premium-feature", context);
```

### Default Context

Set default context that's merged with per-call context:

```csharp
var client = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Context = new ReplaneContext
    {
        ["app_version"] = "2.0.0",
        ["platform"] = "ios"
    }
});
```

### Subscriptions

Subscribe to config changes:

```csharp
// Subscribe to all changes
var unsubscribe = client.Subscribe((name, config) =>
{
    Console.WriteLine($"Config '{name}' updated");
});

// Subscribe to specific config
var unsubscribeSpecific = client.SubscribeConfig("feature-flag", config =>
{
    Console.WriteLine($"Feature flag updated: {config.Value}");
});

// Unsubscribe when done
unsubscribe();
unsubscribeSpecific();
```

### Fallback Values

Provide fallback values for when configs aren't loaded:

```csharp
var client = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Fallbacks = new Dictionary<string, object?>
    {
        ["feature-enabled"] = false,
        ["rate-limit"] = 100
    }
});
```

### Required Configs

Ensure specific configs are present on initialization:

```csharp
var client = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Required = ["essential-config", "api-endpoint"]
});

// ConnectAsync will throw if required configs are missing
await client.ConnectAsync();
```

## Testing

Use the in-memory client for unit tests:

```csharp
using Replane.Testing;

[Fact]
public void TestFeatureFlag()
{
    // Create test client with initial configs
    using var client = TestClient.Create(new Dictionary<string, object?>
    {
        ["feature-enabled"] = true,
        ["max-items"] = 50
    });

    // Use like the real client
    client.Get<bool>("feature-enabled").Should().BeTrue();
    client.Get<int>("max-items").Should().Be(50);
}
```

### Testing with Overrides

```csharp
[Fact]
public void TestOverrides()
{
    using var client = TestClient.Create();

    // Set up config with overrides
    client.SetConfigWithOverrides(
        name: "premium-feature",
        value: false,
        overrides: [
            new OverrideData
            {
                Name = "premium-users",
                Conditions = [
                    new ConditionData
                    {
                        Operator = "equals",
                        Property = "plan",
                        Expected = "premium"
                    }
                ],
                Value = true
            }
        ]);

    // Test with different contexts
    client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "free" })
        .Should().BeFalse();

    client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "premium" })
        .Should().BeTrue();
}
```

### Testing Segmentation

```csharp
[Fact]
public void TestABTest()
{
    using var client = TestClient.Create();

    client.SetConfigWithOverrides(
        name: "ab-test",
        value: "control",
        overrides: [
            new OverrideData
            {
                Name = "treatment-group",
                Conditions = [
                    new ConditionData
                    {
                        Operator = "segmentation",
                        Property = "user_id",
                        FromPercentage = 0,
                        ToPercentage = 50,
                        Seed = "experiment-seed"
                    }
                ],
                Value = "treatment"
            }
        ]);

    // Result is deterministic for each user
    var result = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = "user-123" });
    // Will consistently be either "control" or "treatment" for this user
}
```

## Configuration Options

| Option                    | Type                          | Default  | Description                     |
| ------------------------- | ----------------------------- | -------- | ------------------------------- |
| `BaseUrl`                 | `string`                      | required | Replane server URL              |
| `SdkKey`                  | `string`                      | required | SDK key for authentication      |
| `Context`                 | `ReplaneContext`              | `null`   | Default context for evaluations |
| `Fallbacks`               | `Dictionary<string, object?>` | `null`   | Fallback values                 |
| `Required`                | `IReadOnlyList<string>`       | `null`   | Required config names           |
| `RequestTimeoutMs`        | `int`                         | `2000`   | HTTP request timeout            |
| `InitializationTimeoutMs` | `int`                         | `5000`   | Initial connection timeout      |
| `RetryDelayMs`            | `int`                         | `200`    | Initial retry delay             |
| `InactivityTimeoutMs`     | `int`                         | `30000`  | SSE inactivity timeout          |
| `HttpClient`              | `HttpClient`                  | `null`   | Custom HttpClient               |
| `Debug`                   | `bool`                        | `false`  | Enable debug logging            |

## Condition Operators

The SDK supports the following condition operators for overrides:

| Operator                | Description                |
| ----------------------- | -------------------------- |
| `equals`                | Exact match                |
| `in`                    | Value is in list           |
| `not_in`                | Value is not in list       |
| `less_than`             | Less than comparison       |
| `less_than_or_equal`    | Less than or equal         |
| `greater_than`          | Greater than comparison    |
| `greater_than_or_equal` | Greater than or equal      |
| `segmentation`          | Percentage-based bucketing |
| `and`                   | All conditions must match  |
| `or`                    | Any condition must match   |
| `not`                   | Negate a condition         |

## Error Handling

```csharp
try
{
    await client.ConnectAsync();
    var value = client.Get<string>("my-config");
}
catch (AuthenticationException)
{
    // Invalid SDK key
}
catch (ConfigNotFoundException ex)
{
    // Config doesn't exist
    Console.WriteLine($"Config not found: {ex.ConfigName}");
}
catch (ReplaneTimeoutException ex)
{
    // Operation timed out
    Console.WriteLine($"Timeout after {ex.TimeoutMs}ms");
}
catch (ReplaneException ex)
{
    // Other errors
    Console.WriteLine($"Error [{ex.Code}]: {ex.Message}");
}
```

## Examples

See the [examples](./examples/) directory for complete working examples:

| Example                                                  | Description                                        |
| -------------------------------------------------------- | -------------------------------------------------- |
| [BasicUsage](./examples/BasicUsage/)                     | Simple console app with basic config reading       |
| [ConsoleWithOverrides](./examples/ConsoleWithOverrides/) | Context-based overrides and user segmentation      |
| [BackgroundWorker](./examples/BackgroundWorker/)         | Long-running service with real-time config updates |
| [WebApiIntegration](./examples/WebApiIntegration/)       | ASP.NET Core Web API with middleware and DI        |
| [UnitTesting](./examples/UnitTesting/)                   | Unit testing with the in-memory test client        |

Each example is self-contained and can be copied and run independently.

## License

MIT
