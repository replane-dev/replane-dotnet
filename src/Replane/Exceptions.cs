namespace Replane;

/// <summary>
/// Base exception for all Replane SDK errors.
/// </summary>
public class ReplaneException : Exception
{
    /// <summary>Error code identifying the type of error.</summary>
    public ErrorCode Code { get; }

    public ReplaneException(ErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public override string ToString()
    {
        var result = $"[{Code}] {Message}";
        if (InnerException != null)
        {
            result += $" (caused by: {InnerException.Message})";
        }
        return result;
    }
}

/// <summary>
/// Raised when a requested config does not exist.
/// </summary>
public class ConfigNotFoundException : ReplaneException
{
    /// <summary>The name of the config that was not found.</summary>
    public string ConfigName { get; }

    public ConfigNotFoundException(string configName, Exception? innerException = null)
        : base(ErrorCode.NotFound, $"Config '{configName}' not found", innerException)
    {
        ConfigName = configName;
    }
}

/// <summary>
/// Raised when an operation times out.
/// </summary>
public class ReplaneTimeoutException : ReplaneException
{
    /// <summary>The timeout duration in milliseconds.</summary>
    public int? TimeoutMs { get; }

    public ReplaneTimeoutException(string message = "Operation timed out", int? timeoutMs = null, Exception? innerException = null)
        : base(ErrorCode.Timeout, message, innerException)
    {
        TimeoutMs = timeoutMs;
    }
}

/// <summary>
/// Raised when authentication fails (invalid SDK key).
/// </summary>
public class AuthenticationException : ReplaneException
{
    public AuthenticationException(string message = "Authentication failed - check your SDK key", Exception? innerException = null)
        : base(ErrorCode.AuthError, message, innerException)
    {
    }
}

/// <summary>
/// Raised when a network request fails.
/// </summary>
public class NetworkException : ReplaneException
{
    public NetworkException(string message = "Network request failed", Exception? innerException = null)
        : base(ErrorCode.NetworkError, message, innerException)
    {
    }
}

/// <summary>
/// Raised when attempting operations on a closed client.
/// </summary>
public class ClientClosedException : ReplaneException
{
    public ClientClosedException(Exception? innerException = null)
        : base(ErrorCode.Closed, "Client has been closed", innerException)
    {
    }
}

/// <summary>
/// Raised when the client hasn't finished initialization.
/// </summary>
public class NotInitializedException : ReplaneException
{
    public NotInitializedException(Exception? innerException = null)
        : base(ErrorCode.NotInitialized, "Client has not been initialized - call ConnectAsync first", innerException)
    {
    }
}

/// <summary>
/// Helper methods for creating exceptions from HTTP status codes.
/// </summary>
public static class ExceptionHelper
{
    public static ReplaneException FromHttpStatus(int status, string? message = null, Exception? innerException = null)
    {
        return status switch
        {
            401 => new AuthenticationException(message ?? "Invalid SDK key", innerException),
            403 => new ReplaneException(ErrorCode.Forbidden, message ?? "Access forbidden", innerException),
            404 => new ReplaneException(ErrorCode.NotFound, message ?? "Resource not found", innerException),
            >= 400 and < 500 => new ReplaneException(ErrorCode.ClientError, message ?? $"Client error (HTTP {status})", innerException),
            >= 500 => new ReplaneException(ErrorCode.ServerError, message ?? $"Server error (HTTP {status})", innerException),
            _ => new ReplaneException(ErrorCode.Unknown, message ?? $"Unexpected HTTP status: {status}", innerException)
        };
    }
}
