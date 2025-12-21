namespace Replane;

/// <summary>
/// Interface for Replane clients.
/// Both the real ReplaneClient and the testing InMemoryReplaneClient implement this interface,
/// making it easy to swap implementations for testing or dependency injection.
/// </summary>
public interface IReplaneClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Whether the client has been initialized with configs from the server.
    /// For in-memory clients, this is always true.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Event raised when any config is changed.
    /// </summary>
    event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    /// Get a config value.
    /// This is a synchronous read from the local cache.
    /// Override evaluation happens locally using the provided context.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="name">Config name to retrieve.</param>
    /// <param name="context">Context for override evaluation (merged with default).</param>
    /// <param name="defaultValue">Default value if config doesn't exist.</param>
    /// <returns>The config value with overrides applied.</returns>
    T? Get<T>(string name, ReplaneContext? context = null, T? defaultValue = default);

    /// <summary>
    /// Get a config value as object.
    /// </summary>
    /// <param name="name">Config name to retrieve.</param>
    /// <param name="context">Context for override evaluation (merged with default).</param>
    /// <param name="defaultValue">Default value if config doesn't exist.</param>
    /// <returns>The config value with overrides applied.</returns>
    object? Get(string name, ReplaneContext? context = null, object? defaultValue = null);

    /// <summary>
    /// Close the client and stop any active connections.
    /// </summary>
    void Close();
}
