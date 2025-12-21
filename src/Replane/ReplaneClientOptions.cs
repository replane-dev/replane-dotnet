namespace Replane;

/// <summary>
/// Configuration options for the Replane client.
/// </summary>
public sealed class ReplaneClientOptions
{
    /// <summary>Base URL of the Replane server.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>SDK key for authentication.</summary>
    public required string SdkKey { get; init; }

    /// <summary>Default context for override evaluation.</summary>
    public ReplaneContext? Context { get; init; }

    /// <summary>Fallback values for configs if not loaded from server.</summary>
    public Dictionary<string, object?>? Fallbacks { get; init; }

    /// <summary>List of config names that must be present on init.</summary>
    public IReadOnlyList<string>? Required { get; init; }

    /// <summary>Timeout for HTTP requests in milliseconds. Default: 2000ms.</summary>
    public int RequestTimeoutMs { get; init; } = 2000;

    /// <summary>Timeout for initial connection in milliseconds. Default: 5000ms.</summary>
    public int InitializationTimeoutMs { get; init; } = 5000;

    /// <summary>Initial delay between retries in milliseconds. Default: 200ms.</summary>
    public int RetryDelayMs { get; init; } = 200;

    /// <summary>Max time without SSE events before reconnect in milliseconds. Default: 30000ms.</summary>
    public int InactivityTimeoutMs { get; init; } = 30000;

    /// <summary>Custom HttpClient to use for requests. If null, a new one will be created.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Enable debug logging. Default: false.</summary>
    public bool Debug { get; init; } = false;

    /// <summary>Custom logger implementation. If null, uses console logger when Debug is true, otherwise no logging.</summary>
    public IReplaneLogger? Logger { get; init; }
}
