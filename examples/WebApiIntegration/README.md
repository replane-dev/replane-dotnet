# ASP.NET Core Web API Integration

Demonstrates integrating Replane into an ASP.NET Core Web API with:

- Dependency injection
- Feature flags middleware
- Per-request context evaluation

## Prerequisites

- .NET 8.0 SDK or later
- A running Replane server
- An SDK key from your Replane project

## Setup

1. Copy this directory to your local machine:

   ```bash
   cp -r WebApiIntegration ~/my-replane-example
   cd ~/my-replane-example
   ```

2. Set your environment variables:

   ```bash
   export REPLANE_BASE_URL="https://your-replane-server.com"
   export REPLANE_SDK_KEY="your-sdk-key"
   ```

   Or use `appsettings.json`:

   ```json
   {
     "Replane": {
       "BaseUrl": "https://your-replane-server.com",
       "SdkKey": "your-sdk-key"
     }
   }
   ```

3. Restore packages:
   ```bash
   dotnet restore
   ```

## Running

```bash
dotnet run
```

The API will start at `http://localhost:5000` (or the port shown in console).

Open Swagger UI at: `http://localhost:5000/swagger`

## API Endpoints

### GET /

Returns the welcome message from config.

```bash
curl http://localhost:5000/
# {"message":"Welcome to the API!"}
```

### GET /health

Health check endpoint (bypasses maintenance mode).

```bash
curl http://localhost:5000/health
# {"status":"healthy"}
```

### GET /config/{name}

Get a specific config value.

```bash
curl http://localhost:5000/config/api-rate-limit
# {"name":"api-rate-limit","value":100}
```

### GET /features

Get user-specific features based on context from headers.

```bash
# Free user
curl http://localhost:5000/features \
  -H "X-User-Id: user-123" \
  -H "X-User-Plan: free"
# {"userId":"user-123","plan":"free","features":{"premiumFeatureEnabled":false,"rateLimit":100}}

# Premium user
curl http://localhost:5000/features \
  -H "X-User-Id: user-456" \
  -H "X-User-Plan: premium"
# {"userId":"user-456","plan":"premium","features":{"premiumFeatureEnabled":true,"rateLimit":1000}}
```

## What This Example Demonstrates

### Singleton Registration

Register the Replane client as a singleton service:

```csharp
builder.Services.AddSingleton(replaneClient);
```

### Startup Connection

Connect to Replane during app startup:

```csharp
try
{
    await replaneClient.ConnectAsync();
}
catch (ReplaneException ex)
{
    // Fall back to default values
}
```

### Middleware Integration

Use Replane for middleware decisions:

```csharp
app.Use(async (context, next) =>
{
    var client = context.RequestServices.GetRequiredService<ReplaneClient>();
    var maintenanceMode = client.Get<bool>("maintenance-mode");

    if (maintenanceMode)
    {
        context.Response.StatusCode = 503;
        return;
    }

    await next();
});
```

### Per-Request Context

Build context from request headers/JWT:

```csharp
app.MapGet("/features", (HttpContext http, ReplaneClient client) =>
{
    var context = new ReplaneContext
    {
        ["user_id"] = http.Request.Headers["X-User-Id"].FirstOrDefault(),
        ["plan"] = http.Request.Headers["X-User-Plan"].FirstOrDefault()
    };

    return client.Get<bool>("premium-feature", context);
});
```

### Graceful Shutdown

Dispose the client on shutdown:

```csharp
lifetime.ApplicationStopping.Register(() =>
{
    replaneClient.Dispose();
});
```

## Best Practices

1. **Register as singleton** - The client maintains a persistent connection
2. **Connect at startup** - Don't connect on first request
3. **Handle connection failures** - Use defaults for resilience
4. **Build context from request** - User ID, plan, region from JWT/headers
5. **Dispose on shutdown** - Clean up the SSE connection

## Common Patterns

### Feature Flags

```csharp
if (client.Get<bool>("new-search-enabled", userContext))
{
    return await NewSearchAsync();
}
return await OldSearchAsync();
```

### Rate Limiting

```csharp
var rateLimit = client.Get<int>("api-rate-limit", userContext);
// Apply rate limit
```

### A/B Testing

```csharp
var variant = client.Get<string>("checkout-flow", userContext);
// "variant-a" or "variant-b" based on user segmentation
```
