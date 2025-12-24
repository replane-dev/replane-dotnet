# Replane .NET SDK

Official .NET SDK for [Replane](https://replane.dev) - Feature flags and remote configuration.

[![NuGet](https://img.shields.io/nuget/v/Replane)](https://www.nuget.org/packages/Replane)
[![CI](https://github.com/replane-dev/replane-dotnet/actions/workflows/publish.yml/badge.svg)](https://github.com/replane-dev/replane-dotnet/actions)
[![License](https://img.shields.io/github/license/replane-dev/replane-dotnet)](https://github.com/replane-dev/replane-dotnet/blob/main/LICENSE)
[![Community](https://img.shields.io/badge/discussions-join-blue?logo=github)](https://github.com/orgs/replane-dev/discussions)

## Installation

```bash
dotnet add package Replane
```

## Quick Start

```csharp
using Replane;

// Create and connect
await using var replane = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-replane-server.com",
    SdkKey = "your-sdk-key"
});

await replane.ConnectAsync();

// Get a config value
var featureEnabled = replane.Get<bool>("feature-enabled");
var maxItems = replane.Get<int>("max-items", defaultValue: 100);
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
var enabled = replane.Get<bool>("feature-enabled");
var limit = replane.Get<int>("rate-limit");
var apiKey = replane.Get<string>("api-key");

// With default values
var timeout = replane.Get<int>("timeout-ms", defaultValue: 5000);
```

### Complex Types

Configs can store complex objects that are deserialized on demand:

```csharp
// Define your config type
public record ThemeConfig
{
    public bool DarkMode { get; init; }
    public string PrimaryColor { get; init; } = "";
    public int FontSize { get; init; }
}

// Get complex config
var theme = replane.Get<ThemeConfig>("theme");
Console.WriteLine($"Dark mode: {theme.DarkMode}, Color: {theme.PrimaryColor}");

// Works with overrides too - different themes for different users
var userTheme = replane.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "premium" });
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
var premiumFeature = replane.Get<bool>("premium-feature", context);
```

### Default Context

Set default context that's merged with per-call context:

```csharp
var replane = new ReplaneClient(new ReplaneClientOptions
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

### Real-time Updates

Subscribe to config changes using the `ConfigChanged` event:

```csharp
// Subscribe to all config changes
replane.ConfigChanged += (sender, e) =>
{
    Console.WriteLine($"Config '{e.ConfigName}' updated");
};

// Get typed value from the event
replane.ConfigChanged += (sender, e) =>
{
    if (e.ConfigName == "feature-flag")
    {
        var enabled = e.GetValue<bool>();
        Console.WriteLine($"Feature flag changed to: {enabled}");
    }
};

// Works with complex types too
replane.ConfigChanged += (sender, e) =>
{
    if (e.ConfigName == "theme")
    {
        var theme = e.GetValue<ThemeConfig>();
        Console.WriteLine($"Theme updated: dark={theme?.DarkMode}");
    }
};

// Unsubscribe when needed
void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
{
    Console.WriteLine($"Config changed: {e.ConfigName}");
}

replane.ConfigChanged += OnConfigChanged;
// Later...
replane.ConfigChanged -= OnConfigChanged;
```

### Default Values

Provide default values for when configs aren't loaded:

```csharp
var replane = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Defaults = new Dictionary<string, object?>
    {
        ["feature-enabled"] = false,
        ["rate-limit"] = 100
    }
});
```

### Required Configs

Ensure specific configs are present on initialization:

```csharp
var replane = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Required = ["essential-config", "api-endpoint"]
});

// ConnectAsync will throw if required configs are missing
await replane.ConnectAsync();
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

### Testing Config Changes

```csharp
[Fact]
public void TestConfigChangeEvent()
{
    using var client = TestClient.Create();

    var receivedEvents = new List<ConfigChangedEventArgs>();
    client.ConfigChanged += (sender, e) => receivedEvents.Add(e);

    client.Set("feature", true);
    client.Set("feature", false);

    receivedEvents.Should().HaveCount(2);
    receivedEvents[0].GetValue<bool>().Should().BeTrue();
    receivedEvents[1].GetValue<bool>().Should().BeFalse();
}
```

### Testing Complex Types

```csharp
public record FeatureFlags
{
    public bool NewUI { get; init; }
    public List<string> EnabledModules { get; init; } = [];
}

[Fact]
public void TestComplexType()
{
    var flags = new FeatureFlags
    {
        NewUI = true,
        EnabledModules = ["dashboard", "analytics"]
    };

    using var client = TestClient.Create(new Dictionary<string, object?>
    {
        ["features"] = flags
    });

    var result = client.Get<FeatureFlags>("features");

    result!.NewUI.Should().BeTrue();
    result.EnabledModules.Should().Contain("dashboard");
}

[Fact]
public void TestComplexTypeWithOverrides()
{
    using var client = TestClient.Create();

    var defaultTheme = new ThemeConfig { DarkMode = false, PrimaryColor = "#000", FontSize = 12 };
    var premiumTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#FFD700", FontSize = 16 };

    client.SetConfigWithOverrides(
        name: "theme",
        value: defaultTheme,
        overrides: [
            new OverrideData
            {
                Name = "premium-theme",
                Conditions = [
                    new ConditionData { Operator = "equals", Property = "plan", Expected = "premium" }
                ],
                Value = premiumTheme
            }
        ]);

    client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "free" })!
        .DarkMode.Should().BeFalse();

    client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "premium" })!
        .DarkMode.Should().BeTrue();
}
```

## Dependency Injection

Both `ReplaneClient` and `InMemoryReplaneClient` implement the `IReplaneClient` interface, making it easy to swap implementations for testing or use with dependency injection:

### ASP.NET Core Registration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register Replane client as the interface
builder.Services.AddSingleton<IReplaneClient>(sp =>
{
    var client = new ReplaneClient(new ReplaneClientOptions
    {
        BaseUrl = builder.Configuration["Replane:BaseUrl"]!,
        SdkKey = builder.Configuration["Replane:SdkKey"]!
    });
    return client;
});

var app = builder.Build();

// Connect on startup
var replane = app.Services.GetRequiredService<IReplaneClient>();
if (replane is ReplaneClient realClient)
{
    await realClient.ConnectAsync();
}

// Use in controllers/services
app.MapGet("/api/items", (IReplaneClient replane) =>
{
    var maxItems = replane.Get<int>("max-items", defaultValue: 100);
    return Results.Ok(new { maxItems });
});

app.Run();
```

### Using in Services

```csharp
public class FeatureService
{
    private readonly IReplaneClient _replane;

    public FeatureService(IReplaneClient replane)
    {
        _replane = replane;
    }

    public bool IsFeatureEnabled(string userId)
    {
        return _replane.Get<bool>("new-feature", new ReplaneContext
        {
            ["user_id"] = userId
        });
    }
}
```

### Testing with DI

```csharp
[Fact]
public void TestFeatureService()
{
    // Create test client implementing IReplaneClient
    using var testClient = TestClient.Create(new Dictionary<string, object?>
    {
        ["new-feature"] = true
    });

    // Inject into service
    var service = new FeatureService(testClient);

    // Test the service
    service.IsFeatureEnabled("user-123").Should().BeTrue();
}
```

## Configuration Options

| Option                    | Type                          | Default  | Description                     |
| ------------------------- | ----------------------------- | -------- | ------------------------------- |
| `BaseUrl`                 | `string`                      | required | Replane server URL              |
| `SdkKey`                  | `string`                      | required | SDK key for authentication      |
| `Context`                 | `ReplaneContext`              | `null`   | Default context for evaluations |
| `Defaults`                | `Dictionary<string, object?>` | `null`   | Default values                  |
| `Required`                | `IReadOnlyList<string>`       | `null`   | Required config names           |
| `RequestTimeoutMs`        | `int`                         | `2000`   | HTTP request timeout            |
| `InitializationTimeoutMs` | `int`                         | `5000`   | Initial connection timeout      |
| `RetryDelayMs`            | `int`                         | `200`    | Initial retry delay             |
| `InactivityTimeoutMs`     | `int`                         | `30000`  | SSE inactivity timeout          |
| `HttpClient`              | `HttpClient`                  | `null`   | Custom HttpClient               |
| `Debug`                   | `bool`                        | `false`  | Enable debug logging            |
| `Logger`                  | `IReplaneLogger`              | `null`   | Custom logger implementation    |
| `Agent`                   | `string`                      | `null`   | Agent identifier for User-Agent |

## Debug Logging

Enable debug logging to troubleshoot issues:

```csharp
var replane = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Debug = true
});
```

This outputs detailed logs including:

- Client initialization with all options
- SSE connection lifecycle (connect, reconnect, disconnect)
- Every `Get()` call with config name, context, and result
- Override evaluation details (which conditions matched/failed)
- Raw SSE event data

Example output:

```
[DEBUG] Replane: Initializing ReplaneClient with options:
[DEBUG] Replane:   BaseUrl: https://your-server.com
[DEBUG] Replane:   SdkKey: your...key
[DEBUG] Replane: Connecting to SSE: https://your-server.com/api/sdk/v1/replication/stream
[DEBUG] Replane: SSE event received: type=init
[DEBUG] Replane: Initialization complete: 5 configs loaded
[DEBUG] Replane: Get<Boolean>("feature-flag") called
[DEBUG] Replane:   Config "feature-flag" found, base value: false, overrides: 1
[DEBUG] Replane:     Evaluating override #0 (conditions: property(plan equals "premium"))
[DEBUG] Replane:       Condition: property "plan" ("premium") equals "premium" => Matched
[DEBUG] Replane:   Override #0 matched, returning: true
```

### Custom Logger

Provide your own logger implementation:

```csharp
public class MyLogger : IReplaneLogger
{
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        // Forward to your logging framework
        _logger.Log(MapLevel(level), exception, message);
    }
}

var replane = new ReplaneClient(new ReplaneClientOptions
{
    BaseUrl = "https://your-server.com",
    SdkKey = "your-key",
    Logger = new MyLogger()
});
```

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
    await replane.ConnectAsync();
    var value = replane.Get<string>("my-config");
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

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and contribution guidelines.

## Community

Have questions or want to discuss Replane? Join the conversation in [GitHub Discussions](https://github.com/orgs/replane-dev/discussions).

## License

MIT
