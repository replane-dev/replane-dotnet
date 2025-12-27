namespace Replane;

/// <summary>
/// Configuration options for the Replane client.
/// Connection options (BaseUrl, SdkKey, timeouts) are provided via ConnectAsync.
/// </summary>
public sealed class ReplaneClientOptions
{
    /// <summary>Default context for override evaluation.</summary>
    public ReplaneContext? Context { get; init; }

    /// <summary>Default values for configs if not loaded from server.</summary>
    public Dictionary<string, object?>? Defaults { get; init; }

    /// <summary>List of config names that must be present on init.</summary>
    public IReadOnlyList<string>? Required { get; init; }

    /// <summary>Custom HttpClient to use for requests. If null, a new one will be created.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Enable debug logging. Default: false.</summary>
    public bool Debug { get; init; } = false;

    /// <summary>Custom logger implementation. If null, uses console logger when Debug is true, otherwise no logging.</summary>
    public IReplaneLogger? Logger { get; init; }
}
