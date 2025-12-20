# Unit Testing Example

Demonstrates how to unit test code that uses Replane, using the in-memory test client.

## Prerequisites

- .NET 8.0 SDK or later

## Setup

1. Copy this directory to your local machine:
   ```bash
   cp -r UnitTesting ~/my-replane-example
   cd ~/my-replane-example
   ```

2. Restore packages:
   ```bash
   dotnet restore
   ```

## Running Tests

```bash
dotnet test
```

Or with verbose output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

## What This Example Demonstrates

### Using the In-Memory Test Client

The `InMemoryReplaneClient` provides the same interface as the real client but stores configs in memory:

```csharp
using var config = TestClient.Create(new Dictionary<string, object?>
{
    ["feature-enabled"] = true,
    ["rate-limit"] = 100
});
```

### Testing with Simple Configs

```csharp
[Fact]
public void Test_WithSimpleConfig()
{
    using var config = TestClient.Create(new Dictionary<string, object?>
    {
        ["discounts-enabled"] = true,
        ["base-discount-percent"] = 10.0
    });

    var service = new PricingService(config);
    var result = service.CalculateDiscount("user-1", "free", 100m);

    Assert.Equal(10m, result);
}
```

### Testing with Overrides

Test conditional logic with override rules:

```csharp
[Fact]
public void Test_WithOverrides()
{
    using var config = TestClient.Create();

    config.SetConfigWithOverrides(
        "premium-bonus",
        value: 0.0,  // Default for non-premium
        overrides:
        [
            new OverrideData
            {
                Name = "premium-users",
                Conditions =
                [
                    new ConditionData
                    {
                        Operator = "in",
                        Property = "plan",
                        Expected = new List<object?> { "premium", "enterprise" }
                    }
                ],
                Value = 10.0  // Bonus for premium users
            }
        ]);

    var service = new PricingService(config);

    // Free user gets default
    Assert.Equal(0.0, service.GetBonus("user-1", "free"));

    // Premium user gets override value
    Assert.Equal(10.0, service.GetBonus("user-2", "premium"));
}
```

### Testing Segmentation (Gradual Rollouts)

```csharp
[Fact]
public void Test_Segmentation_IsDeterministic()
{
    using var config = TestClient.Create();

    config.SetConfigWithOverrides(
        "feature",
        value: false,
        overrides:
        [
            new OverrideData
            {
                Name = "rollout",
                Conditions =
                [
                    new ConditionData
                    {
                        Operator = "segmentation",
                        Property = "user_id",
                        FromPercentage = 0,
                        ToPercentage = 50,
                        Seed = "feature-rollout"
                    }
                ],
                Value = true
            }
        ]);

    // Same user always gets same result
    var result1 = config.Get<bool>("feature", new ReplaneContext { ["user_id"] = "user-123" });
    var result2 = config.Get<bool>("feature", new ReplaneContext { ["user_id"] = "user-123" });
    Assert.Equal(result1, result2);
}
```

### Modifying Configs During Tests

Use `Set()` to change configs mid-test:

```csharp
[Fact]
public void Test_ConfigChanges()
{
    using var config = TestClient.Create(new Dictionary<string, object?>
    {
        ["feature-enabled"] = false
    });

    Assert.False(config.Get<bool>("feature-enabled"));

    // Change the config
    config.Set("feature-enabled", true);

    Assert.True(config.Get<bool>("feature-enabled"));
}
```

## Supported Condition Operators

The test client supports all condition types:

| Operator | Description | Example |
|----------|-------------|---------|
| `equals` | Exact match | `plan == "premium"` |
| `in` | Value in list | `region in ["us-east", "us-west"]` |
| `not_in` | Value not in list | `status not in ["blocked"]` |
| `less_than` | Numeric comparison | `age < 18` |
| `less_than_or_equal` | Numeric comparison | `age <= 18` |
| `greater_than` | Numeric comparison | `score > 100` |
| `greater_than_or_equal` | Numeric comparison | `score >= 100` |
| `segmentation` | Percentage bucketing | 50% of users |
| `and` | All conditions match | Multiple conditions |
| `or` | Any condition matches | Multiple conditions |
| `not` | Negate condition | Inverse logic |

## Best Practices

1. **Use `using` statements** - Dispose the client after each test
2. **Isolate tests** - Create a fresh client for each test
3. **Test edge cases** - Test default values, missing configs
4. **Test overrides** - Verify conditional logic works correctly
5. **Test segmentation distribution** - Verify rollout percentages

## Project Structure

```
UnitTesting/
├── Services/
│   └── PricingService.cs    # Example service using Replane
├── PricingServiceTests.cs   # Unit tests
├── UnitTesting.csproj       # Project file
└── README.md
```
