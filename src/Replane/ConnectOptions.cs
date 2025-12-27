namespace Replane;

/// <summary>
/// Connection options for ConnectAsync.
/// </summary>
public sealed class ConnectOptions
{
    /// <summary>Base URL of the Replane server.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>SDK key for authentication.</summary>
    public required string SdkKey { get; init; }

    /// <summary>Timeout for HTTP requests in milliseconds. Default: 2000ms.</summary>
    public int RequestTimeoutMs { get; init; } = 2000;

    /// <summary>Timeout for initial connection in milliseconds. Default: 5000ms.</summary>
    public int ConnectionTimeoutMs { get; init; } = 5000;

    /// <summary>Initial delay between retries in milliseconds. Default: 200ms.</summary>
    public int RetryDelayMs { get; init; } = 200;

    /// <summary>Max time without SSE events before reconnect in milliseconds. Default: 30000ms.</summary>
    public int InactivityTimeoutMs { get; init; } = 30000;

    /// <summary>Agent identifier sent in User-Agent header. Defaults to SDK identifier.</summary>
    public string? Agent { get; init; }
}
