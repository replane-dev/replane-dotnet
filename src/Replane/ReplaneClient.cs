using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Replane;

/// <summary>
/// Event arguments for config change events.
/// </summary>
public sealed class ConfigChangedEventArgs : EventArgs
{
    /// <summary>
    /// The name of the config that changed.
    /// </summary>
    public required string ConfigName { get; init; }

    /// <summary>
    /// The updated config with its new value and overrides.
    /// </summary>
    public required Config Config { get; init; }

    /// <summary>
    /// Gets the base value deserialized to the specified type.
    /// </summary>
    public T? GetValue<T>() => Config.GetValue<T>();
}

/// <summary>
/// Replane client for fetching and subscribing to configuration changes.
/// Maintains a persistent SSE connection to receive real-time config updates.
/// Config reads are synchronous and return immediately from the local cache.
/// </summary>
public sealed class ReplaneClient : IDisposable, IAsyncDisposable
{
    private readonly ReplaneClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IReplaneLogger _logger;

    private readonly Dictionary<string, Config> _configs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private bool _closed;
    private bool _initialized;
    private ReplaneException? _initError;
    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;

    /// <summary>
    /// Event raised when any config is changed.
    /// </summary>
    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    /// Creates a new Replane client.
    /// </summary>
    public ReplaneClient(ReplaneClientOptions options, IReplaneLogger? logger = null)
    {
        _options = options;
        _logger = logger ?? (options.Debug ? ConsoleReplaneLogger.Instance : NullReplaneLogger.Instance);

        _logger.LogDebug($"Initializing ReplaneClient with options:");
        _logger.LogDebug($"  BaseUrl: {options.BaseUrl}");
        _logger.LogDebug($"  SdkKey: {MaskSdkKey(options.SdkKey)}");
        _logger.LogDebug($"  RequestTimeoutMs: {options.RequestTimeoutMs}");
        _logger.LogDebug($"  InitializationTimeoutMs: {options.InitializationTimeoutMs}");
        _logger.LogDebug($"  RetryDelayMs: {options.RetryDelayMs}");
        _logger.LogDebug($"  InactivityTimeoutMs: {options.InactivityTimeoutMs}");
        _logger.LogDebug($"  Debug: {options.Debug}");
        if (options.Context != null && options.Context.Count > 0)
        {
            _logger.LogDebug($"  Default context: {FormatContext(options.Context)}");
        }
        if (options.Fallbacks != null && options.Fallbacks.Count > 0)
        {
            _logger.LogDebug($"  Fallbacks: [{string.Join(", ", options.Fallbacks.Keys)}]");
        }
        if (options.Required != null && options.Required.Count > 0)
        {
            _logger.LogDebug($"  Required configs: [{string.Join(", ", options.Required)}]");
        }

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
            _logger.LogDebug("  Using provided HttpClient");
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan // We handle timeouts ourselves
            };
            _ownsHttpClient = true;
            _logger.LogDebug("  Created new HttpClient");
        }

        // Initialize fallbacks
        if (options.Fallbacks != null)
        {
            foreach (var (name, value) in options.Fallbacks)
            {
                var jsonValue = JsonValueConverter.ToJsonElement(value);
                _configs[name] = new Config { Name = name, Value = jsonValue };
                _logger.LogDebug($"  Registered fallback: {name} = {FormatValue(value)}");
            }
        }

        _logger.LogDebug("ReplaneClient initialized");
    }

    /// <summary>
    /// Whether the client has been initialized with configs from the server.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Connect to the Replane server and start receiving updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            throw new ClientClosedException();
        }

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _streamTask = RunStreamAsync(_streamCts.Token);

        // Wait for initialization
        using var timeoutCts = new CancellationTokenSource(_options.InitializationTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _initTcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new ReplaneTimeoutException(
                $"Initialization timed out after {_options.InitializationTimeoutMs}ms",
                _options.InitializationTimeoutMs);
        }

        if (_initError != null)
        {
            throw _initError;
        }
    }

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
    public T? Get<T>(string name, ReplaneContext? context = null, T? defaultValue = default)
    {
        _logger.LogDebug($"Get<{typeof(T).Name}>(\"{name}\") called");

        if (_closed)
        {
            _logger.LogDebug($"  Client is closed, throwing ClientClosedException");
            throw new ClientClosedException();
        }

        var mergedContext = (_options.Context ?? []).Merge(context);
        if (mergedContext.Count > 0)
        {
            _logger.LogDebug($"  Merged context: {FormatContext(mergedContext)}");
        }

        lock (_lock)
        {
            if (!_configs.TryGetValue(name, out var config))
            {
                if (defaultValue is not null || typeof(T).IsValueType)
                {
                    _logger.LogDebug($"  Config \"{name}\" not found, returning default: {FormatValue(defaultValue)}");
                    return defaultValue;
                }
                _logger.LogDebug($"  Config \"{name}\" not found, throwing ConfigNotFoundException");
                throw new ConfigNotFoundException(name);
            }

            _logger.LogDebug($"  Config \"{name}\" found, base value: {FormatValue(config.Value)}, overrides: {config.Overrides.Count}");

            var (result, matchedOverrideIndex) = Evaluator.EvaluateConfigWithDetails(config, mergedContext, _logger);

            if (matchedOverrideIndex >= 0)
            {
                _logger.LogDebug($"  Override #{matchedOverrideIndex} matched, returning: {FormatValue(result)}");
            }
            else
            {
                _logger.LogDebug($"  No override matched, returning base value: {FormatValue(result)}");
            }

            var converted = ConvertValue<T>(result);
            _logger.LogDebug($"  Final value (converted to {typeof(T).Name}): {FormatValue(converted)}");
            return converted;
        }
    }

    /// <summary>
    /// Get a config value as object.
    /// </summary>
    public object? Get(string name, ReplaneContext? context = null, object? defaultValue = null)
    {
        return Get<object>(name, context, defaultValue);
    }

    /// <summary>
    /// Close the client and stop the SSE connection.
    /// </summary>
    public void Close()
    {
        _logger.LogDebug("Close() called");
        if (_closed)
        {
            _logger.LogDebug("  Client already closed, skipping");
            return;
        }
        _closed = true;

        _logger.LogDebug("  Cancelling SSE stream...");
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;

        if (_ownsHttpClient)
        {
            _logger.LogDebug("  Disposing HttpClient...");
            _httpClient.Dispose();
        }
        _logger.LogDebug("  Client closed");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Close();
        if (_streamTask != null)
        {
            try
            {
                await _streamTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
    }

    private async Task RunStreamAsync(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        const int maxRetries = 10;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectStreamAsync(cancellationToken);
                retryCount = 0;
            }
            catch (AuthenticationException e)
            {
                // Auth errors are permanent - don't retry
                if (!_initialized)
                {
                    _initError = e;
                    _initTcs.TrySetResult(false);
                }
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_closed || cancellationToken.IsCancellationRequested)
            {
                // Disposed during shutdown
                return;
            }
            catch (IOException) when (_closed || cancellationToken.IsCancellationRequested)
            {
                // Network stream can throw during shutdown
                return;
            }
            catch (Exception ex)
            {
                var error = ex is ReplaneException re ? re : new NetworkException(ex.Message, ex);
                _logger.LogWarning($"SSE connection error: {error.Message}", error);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Exponential backoff
            retryCount = Math.Min(retryCount + 1, maxRetries);
            var delay = TimeSpan.FromMilliseconds(_options.RetryDelayMs * Math.Pow(2, retryCount - 1));
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 30000));

            _logger.LogDebug($"Reconnecting in {delay.TotalSeconds:F1} seconds...");

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectStreamAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var requestUrl = $"{baseUrl}/api/sdk/v1/replication/stream";

        _logger.LogDebug($"Connecting to SSE: {requestUrl}");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SdkKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ReplaneTimeoutException($"Request timed out after {_options.RequestTimeoutMs}ms", _options.RequestTimeoutMs);
        }

        using (response)
        {
            _logger.LogDebug($"Response status: {response.StatusCode}");

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new AuthenticationException();
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw ExceptionHelper.FromHttpStatus((int)response.StatusCode, errorBody);
            }

            _logger.LogDebug("SSE connection established, processing stream...");
            await ProcessStreamAsync(response, cancellationToken);
        }
    }

    private async Task ProcessStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var parser = new SseParser();
        var buffer = new byte[4096];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Set read timeout for inactivity detection
        // Note: We use the inactivity timeout as the read timeout. If no data comes within this time,
        // the read will be cancelled and we'll reconnect.
        using var inactivityCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, inactivityCts.Token);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Reset inactivity timer before each read
            inactivityCts.CancelAfter(_options.InactivityTimeoutMs);

            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, linkedCts.Token);
            }
            catch (OperationCanceledException) when (inactivityCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug($"SSE inactivity timeout after {_options.InactivityTimeoutMs}ms, reconnecting...");
                break;
            }
            catch (ObjectDisposedException)
            {
                // Stream was disposed - connection closed
                _logger.LogDebug("SSE stream disposed");
                break;
            }
            catch (IOException)
            {
                // Network error - connection lost
                _logger.LogDebug("SSE stream IO error");
                break;
            }

            if (bytesRead == 0)
            {
                _logger.LogDebug("SSE stream ended (server closed connection)");
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            _logger.LogDebug($"Received chunk: {bytesRead} bytes");

            foreach (var evt in parser.Feed(text))
            {
                HandleEvent(evt);
            }
        }
    }

    private void HandleEvent(SseEvent evt)
    {
        // Event type can be in SSE 'event:' field or in data.type
        var eventType = evt.EventType;
        if (eventType == null && evt.Data.HasValue)
        {
            if (evt.Data.Value.TryGetProperty("type", out var typeElement))
            {
                eventType = typeElement.GetString();
            }
        }

        _logger.LogDebug($"SSE event received: type={eventType}");

        // Log raw data for debugging
        if (evt.Data.HasValue)
        {
            var rawJson = evt.Data.Value.ToString();
            if (rawJson.Length > 500)
            {
                _logger.LogDebug($"  Raw data (truncated): {rawJson[..500]}...");
            }
            else
            {
                _logger.LogDebug($"  Raw data: {rawJson}");
            }
        }
        else if (evt.RawData != null)
        {
            _logger.LogDebug($"  Raw data (unparsed): {evt.RawData}");
        }

        switch (eventType)
        {
            case "init":
                HandleInit(evt.Data!.Value);
                break;
            case "config_change":
                HandleConfigChange(evt.Data!.Value);
                break;
            default:
                _logger.LogDebug($"Unknown event type: {eventType}");
                break;
        }
    }

    private void HandleInit(JsonElement data)
    {
        _logger.LogDebug("Processing init event...");

        if (!data.TryGetProperty("configs", out var configsElement))
        {
            _logger.LogDebug("  No 'configs' property found in init event data");
            return;
        }

        var configsCount = 0;

        lock (_lock)
        {
            foreach (var configElement in configsElement.EnumerateArray())
            {
                var config = ConfigParser.ParseConfig(configElement);
                _configs[config.Name] = config;
                configsCount++;
                _logger.LogDebug($"  Loaded config: {config.Name}");
                _logger.LogDebug($"    Base value: {FormatValue(config.Value)}");
                _logger.LogDebug($"    Overrides: {config.Overrides.Count}");
                for (var i = 0; i < config.Overrides.Count; i++)
                {
                    var ov = config.Overrides[i];
                    _logger.LogDebug($"      Override #{i}: value={FormatValue(ov.Value)}, conditions={ov.Conditions.Count}");
                }
            }

            // Check required configs
            if (_options.Required != null)
            {
                var missing = _options.Required.Where(r => !_configs.ContainsKey(r)).ToList();
                if (missing.Count > 0)
                {
                    _logger.LogDebug($"  Missing required configs: [{string.Join(", ", missing)}]");
                    _initError = new ConfigNotFoundException($"Missing required configs: {string.Join(", ", missing)}");
                }
            }

            _logger.LogDebug($"  Current configs in cache: [{string.Join(", ", _configs.Keys)}]");
        }

        _initialized = true;
        _initTcs.TrySetResult(true);
        _logger.LogDebug($"Initialization complete: {configsCount} configs loaded");
    }

    private void HandleConfigChange(JsonElement data)
    {
        _logger.LogDebug("Processing config_change event...");

        JsonElement configElement;
        if (data.TryGetProperty("config", out var ce))
        {
            configElement = ce;
        }
        else
        {
            configElement = data;
        }

        var config = ConfigParser.ParseConfig(configElement);

        lock (_lock)
        {
            var hadPrevious = _configs.TryGetValue(config.Name, out var previousConfig);
            _configs[config.Name] = config;

            _logger.LogDebug($"  Config \"{config.Name}\" updated:");
            if (hadPrevious)
            {
                _logger.LogDebug($"    Previous base value: {FormatValue(previousConfig!.Value)}");
            }
            _logger.LogDebug($"    New base value: {FormatValue(config.Value)}");
            _logger.LogDebug($"    Overrides: {config.Overrides.Count}");
        }

        // Raise event outside the lock to avoid deadlocks
        OnConfigChanged(config);

        _logger.LogDebug($"Config change processed: {config.Name}");
    }

    /// <summary>
    /// Raises the ConfigChanged event.
    /// </summary>
    private void OnConfigChanged(Config config)
    {
        var handler = ConfigChanged;
        if (handler == null)
        {
            _logger.LogDebug("  No ConfigChanged event handlers registered");
            return;
        }

        _logger.LogDebug($"  Raising ConfigChanged event for \"{config.Name}\"");

        var args = new ConfigChangedEventArgs
        {
            ConfigName = config.Name,
            Config = config
        };

        try
        {
            handler(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ConfigChanged event handler error: {ex.Message}", ex);
        }
    }

    private static T? ConvertValue<T>(object? value)
    {
        if (value == null)
        {
            return default;
        }

        // Handle JsonElement - use lazy deserialization
        if (value is JsonElement element)
        {
            return JsonValueConverter.Convert<T>(element);
        }

        if (value is T typed)
        {
            return typed;
        }

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private static string MaskSdkKey(string sdkKey)
    {
        if (string.IsNullOrEmpty(sdkKey) || sdkKey.Length <= 8)
        {
            return "****";
        }
        return $"{sdkKey[..4]}...{sdkKey[^4..]}";
    }

    private static string FormatContext(ReplaneContext? context)
    {
        if (context == null || context.Count == 0)
        {
            return "{}";
        }
        var pairs = context.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}");
        return $"{{{string.Join(", ", pairs)}}}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            JsonElement je => je.ToString(),
            _ => value.ToString() ?? "null"
        };
    }
}
