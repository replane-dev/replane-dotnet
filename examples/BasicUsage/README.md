# Basic Usage Example

A simple console application demonstrating basic Replane SDK usage.

## Prerequisites

- .NET 8.0 SDK or later
- A running Replane server
- An SDK key from your Replane project

## Setup

1. Copy this directory to your local machine:

   ```bash
   cp -r BasicUsage ~/my-replane-example
   cd ~/my-replane-example
   ```

2. Set your environment variables:

   ```bash
   export REPLANE_BASE_URL="https://your-replane-server.com"
   export REPLANE_SDK_KEY="your-sdk-key"
   ```

   Or edit `Program.cs` directly to set the values.

3. Restore packages:
   ```bash
   dotnet restore
   ```

## Running

```bash
dotnet run
```

## Expected Output

```
Connecting to Replane server...
Connected successfully!
feature-enabled: false
max-items: 10
timeout-ms: 5000
Expected error: Config 'non-existent-config' not found
```

## What This Example Demonstrates

- Creating and configuring a `ReplaneClient`
- Connecting to the Replane server
- Reading typed config values with `Get<T>()`
- Using default values
- Proper error handling
- Resource cleanup with `DisposeAsync()`

## Key Concepts

### Default Values

Default values are used when:

- The server hasn't sent a config yet
- The config doesn't exist on the server

```csharp
Defaults = new Dictionary<string, object?>
{
    ["feature-enabled"] = false,
    ["max-items"] = 10
}
```

### Default Values

You can also provide a default at read time:

```csharp
// Returns 5000 if "timeout-ms" doesn't exist
var timeout = client.Get<int>("timeout-ms", defaultValue: 5000);
```

### Error Handling

The SDK throws specific exceptions:

- `AuthenticationException` - Invalid SDK key
- `ConfigNotFoundException` - Config not found (and no default provided)
- `ReplaneTimeoutException` - Connection or request timeout
- `ReplaneException` - Base class for all Replane errors
