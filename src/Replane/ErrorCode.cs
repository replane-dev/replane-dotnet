namespace Replane;

/// <summary>
/// Error codes for ReplaneException.
/// </summary>
public enum ErrorCode
{
    /// <summary>Config or resource not found.</summary>
    NotFound,

    /// <summary>Operation timed out.</summary>
    Timeout,

    /// <summary>Network request failed.</summary>
    NetworkError,

    /// <summary>Authentication failed (invalid SDK key).</summary>
    AuthError,

    /// <summary>Access forbidden.</summary>
    Forbidden,

    /// <summary>Server error (5xx).</summary>
    ServerError,

    /// <summary>Client error (4xx).</summary>
    ClientError,

    /// <summary>Client has been closed.</summary>
    Closed,

    /// <summary>Client has not been initialized.</summary>
    NotInitialized,

    /// <summary>Unknown error.</summary>
    Unknown
}
