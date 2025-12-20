using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Replane;

/// <summary>
/// Delegate for config change callbacks.
/// </summary>
public delegate void ConfigChangeCallback(string configName, Config config);

/// <summary>
/// Delegate for specific config change callbacks.
/// </summary>
public delegate void SingleConfigChangeCallback(Config config);

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

    private readonly List<ConfigChangeCallback> _allSubscribers = [];
    private readonly Dictionary<string, List<SingleConfigChangeCallback>> _configSubscribers = new(StringComparer.Ordinal);

    private bool _closed;
    private bool _initialized;
    private ReplaneException? _initError;
    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;

    /// <summary>
    /// Creates a new Replane client.
    /// </summary>
    public ReplaneClient(ReplaneClientOptions options, IReplaneLogger? logger = null)
    {
        _options = options;
        _logger = logger ?? (options.Debug ? ConsoleReplaneLogger.Instance : NullReplaneLogger.Instance);

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan // We handle timeouts ourselves
            };
            _ownsHttpClient = true;
        }

        // Initialize fallbacks
        if (options.Fallbacks != null)
        {
            foreach (var (name, value) in options.Fallbacks)
            {
                _configs[name] = new Config { Name = name, Value = value };
            }
        }
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
        if (_closed)
        {
            throw new ClientClosedException();
        }

        var mergedContext = (_options.Context ?? []).Merge(context);

        lock (_lock)
        {
            if (!_configs.TryGetValue(name, out var config))
            {
                if (defaultValue is not null || typeof(T).IsValueType)
                {
                    return defaultValue;
                }
                throw new ConfigNotFoundException(name);
            }

            var result = Evaluator.EvaluateConfig(config, mergedContext);
            return ConvertValue<T>(result);
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
    /// Subscribe to all config changes.
    /// </summary>
    /// <param name="callback">Function called with (configName, config) on changes.</param>
    /// <returns>Unsubscribe action.</returns>
    public Action Subscribe(ConfigChangeCallback callback)
    {
        lock (_lock)
        {
            _allSubscribers.Add(callback);
        }

        return () =>
        {
            lock (_lock)
            {
                _allSubscribers.Remove(callback);
            }
        };
    }

    /// <summary>
    /// Subscribe to changes for a specific config.
    /// </summary>
    /// <param name="name">Config name to watch.</param>
    /// <param name="callback">Function called with the new config on changes.</param>
    /// <returns>Unsubscribe action.</returns>
    public Action SubscribeConfig(string name, SingleConfigChangeCallback callback)
    {
        lock (_lock)
        {
            if (!_configSubscribers.TryGetValue(name, out var list))
            {
                list = [];
                _configSubscribers[name] = list;
            }
            list.Add(callback);
        }

        return () =>
        {
            lock (_lock)
            {
                if (_configSubscribers.TryGetValue(name, out var list))
                {
                    list.Remove(callback);
                }
            }
        };
    }

    /// <summary>
    /// Close the client and stop the SSE connection.
    /// </summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
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
        var lastEventTime = DateTime.UtcNow;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Use short timeout to check inactivity
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCts.Token);

            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, linkedCts.Token);
            }
            catch (OperationCanceledException) when (readCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Check inactivity timeout
                var elapsed = DateTime.UtcNow - lastEventTime;
                if (elapsed.TotalMilliseconds > _options.InactivityTimeoutMs)
                {
                    _logger.LogDebug("SSE inactivity timeout, reconnecting...");
                    break;
                }
                continue;
            }

            if (bytesRead == 0)
            {
                _logger.LogDebug("SSE stream ended");
                break;
            }

            lastEventTime = DateTime.UtcNow;
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
        if (!data.TryGetProperty("configs", out var configsElement))
        {
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
                _logger.LogDebug($"Loaded config: {config.Name} (overrides={config.Overrides.Count})");
            }

            // Check required configs
            if (_options.Required != null)
            {
                var missing = _options.Required.Where(r => !_configs.ContainsKey(r)).ToList();
                if (missing.Count > 0)
                {
                    _initError = new ConfigNotFoundException($"Missing required configs: {string.Join(", ", missing)}");
                }
            }
        }

        _initialized = true;
        _initTcs.TrySetResult(true);
        _logger.LogDebug($"Initialization complete: {configsCount} configs loaded");
    }

    private void HandleConfigChange(JsonElement data)
    {
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
            _configs[config.Name] = config;

            // Notify subscribers
            foreach (var callback in _allSubscribers.ToList())
            {
                try
                {
                    callback(config.Name, config);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Subscriber callback error: {ex.Message}", ex);
                }
            }

            if (_configSubscribers.TryGetValue(config.Name, out var configCallbacks))
            {
                foreach (var callback in configCallbacks.ToList())
                {
                    try
                    {
                        callback(config);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Subscriber callback error: {ex.Message}", ex);
                    }
                }
            }
        }

        _logger.LogDebug($"Config updated: {config.Name}");
    }

    private static T? ConvertValue<T>(object? value)
    {
        if (value == null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        // Handle JsonElement
        if (value is JsonElement element)
        {
            return element.Deserialize<T>();
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
}
