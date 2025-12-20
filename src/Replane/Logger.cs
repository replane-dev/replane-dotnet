namespace Replane;

/// <summary>
/// Log level for Replane logger.
/// </summary>
public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

/// <summary>
/// Simple logging interface for Replane SDK.
/// </summary>
public interface IReplaneLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

/// <summary>
/// Null logger that discards all messages.
/// </summary>
public sealed class NullReplaneLogger : IReplaneLogger
{
    public static readonly NullReplaneLogger Instance = new();

    private NullReplaneLogger() { }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        // Do nothing
    }
}

/// <summary>
/// Console logger for debug output.
/// </summary>
public sealed class ConsoleReplaneLogger : IReplaneLogger
{
    public static readonly ConsoleReplaneLogger Instance = new();

    private ConsoleReplaneLogger() { }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var prefix = level switch
        {
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Information => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            _ => "[???]"
        };

        Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {prefix} Replane: {message}");
        if (exception != null)
        {
            Console.WriteLine($"  Exception: {exception}");
        }
    }
}

/// <summary>
/// Extension methods for IReplaneLogger.
/// </summary>
public static class ReplaneLoggerExtensions
{
    public static void LogDebug(this IReplaneLogger logger, string message)
        => logger.Log(LogLevel.Debug, message);

    public static void LogDebug(this IReplaneLogger logger, string message, params object?[] args)
        => logger.Log(LogLevel.Debug, string.Format(message.Replace("{", "{{").Replace("}", "}}").Replace("{{", "{0}"), args));

    public static void LogInformation(this IReplaneLogger logger, string message)
        => logger.Log(LogLevel.Information, message);

    public static void LogWarning(this IReplaneLogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Warning, message, exception);

    public static void LogError(this IReplaneLogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Error, message, exception);
}
