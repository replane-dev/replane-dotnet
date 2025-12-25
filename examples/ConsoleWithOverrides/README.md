# Context & Overrides Example

Demonstrates how to use context for override evaluation in Replane.

## Prerequisites

- .NET 8.0 SDK or later
- A running Replane server (optional - works with defaults)
- An SDK key from your Replane project

## Setup

1. Copy this directory to your local machine:

   ```bash
   cp -r ConsoleWithOverrides ~/my-replane-example
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

## What This Example Demonstrates

### Default Context

Set a default context that applies to all config reads:

```csharp
var client = new ReplaneClient(new ReplaneClientOptions
{
    // ...
    Context = new ReplaneContext
    {
        ["app_version"] = "2.0.0",
        ["platform"] = "console"
    }
});
```

### Per-Request Context

Pass additional context when reading configs:

```csharp
var userContext = new ReplaneContext
{
    ["user_id"] = "user-123",
    ["plan"] = "premium"
};

var feature = client.Get<bool>("premium-feature", userContext);
```

### Context Merging

When you provide context to `Get()`, it's merged with the default context:

- Per-request context takes precedence for duplicate keys
- Default context fills in any missing keys

### Override Evaluation

Context is used to evaluate override conditions on the client side:

**Server Configuration (example):**

```json
{
  "name": "premium-feature",
  "value": false,
  "overrides": [
    {
      "name": "premium-users",
      "conditions": [
        {
          "operator": "in",
          "property": "plan",
          "value": ["premium", "enterprise"]
        }
      ],
      "value": true
    }
  ]
}
```

**Client Evaluation:**

```csharp
// Free user - gets base value (false)
client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "free" });

// Premium user - matches override, gets true
client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "premium" });
```

## Key Points

1. **Context never leaves the client** - All evaluation happens locally
2. **Overrides are evaluated in order** - First match wins
3. **Missing context properties** - Result in "unknown" evaluation, override is skipped
4. **Type coercion** - The SDK handles type conversion between context and condition values

## Use Cases

- **Feature flags per user plan**: Enable features for premium users
- **Regional configuration**: Different settings per region
- **A/B testing**: Gradual rollouts based on user ID segmentation
- **Device-specific config**: Different behavior for mobile vs desktop
