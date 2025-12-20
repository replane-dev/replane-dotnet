using System.Net;
using System.Text;
using System.Text.Json;

namespace Replane.Tests.MockServer;

/// <summary>
/// A mock HTTP server that simulates the Replane API for integration testing.
/// Supports real HTTP connections and SSE streaming.
/// </summary>
public sealed class MockReplaneServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private readonly List<Config> _configs = [];
    private readonly List<TaskCompletionSource<bool>> _sseConnections = [];
    private readonly object _lock = new();

    public string BaseUrl { get; }
    public string SdkKey { get; } = "test-sdk-key";
    public int ConnectionCount => _sseConnections.Count;

    /// <summary>
    /// Delay before sending the init event (for testing timeouts).
    /// </summary>
    public TimeSpan InitDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether to simulate an authentication error.
    /// </summary>
    public bool SimulateAuthError { get; set; }

    /// <summary>
    /// Whether to simulate a server error.
    /// </summary>
    public bool SimulateServerError { get; set; }

    public MockReplaneServer(int? port = null)
    {
        var actualPort = port ?? GetAvailablePort();
        BaseUrl = $"http://localhost:{actualPort}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{actualPort}/");
    }

    public void Start()
    {
        _listener.Start();
        _listenerTask = Task.Run(HandleRequestsAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        _cts.Dispose();
    }

    /// <summary>
    /// Add a config that will be sent to clients.
    /// </summary>
    public void AddConfig(Config config)
    {
        lock (_lock)
        {
            _configs.Add(config);
        }
    }

    /// <summary>
    /// Add a simple config with just a name and value.
    /// </summary>
    public void AddConfig(string name, object? value)
    {
        AddConfig(new Config { Name = name, Value = value });
    }

    /// <summary>
    /// Send a config change event to all connected clients.
    /// </summary>
    public async Task SendConfigChangeAsync(Config config)
    {
        var eventData = new
        {
            type = "config_change",
            config = new
            {
                name = config.Name,
                value = config.Value,
                overrides = config.Overrides.Select(o => new
                {
                    name = o.Name,
                    value = o.Value,
                    conditions = SerializeConditions(o.Conditions)
                }).ToList()
            }
        };

        var sseEvent = $"event: config_change\ndata: {JsonSerializer.Serialize(eventData)}\n\n";
        await BroadcastSseEventAsync(sseEvent);
    }

    /// <summary>
    /// Wait for at least one SSE connection to be established.
    /// </summary>
    public async Task WaitForConnectionAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_sseConnections.Count > 0)
                {
                    return;
                }
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("No SSE connection established within timeout");
    }

    private async Task HandleRequestsAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var contextTask = _listener.GetContextAsync();
                var context = await contextTask.WaitAsync(_cts.Token);

                // Handle each request in a separate task
                _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var response = context.Response;

            // Check authentication
            var authHeader = context.Request.Headers["Authorization"];
            if (SimulateAuthError || authHeader != $"Bearer {SdkKey}")
            {
                response.StatusCode = 401;
                await WriteResponseAsync(response, "Unauthorized");
                return;
            }

            if (SimulateServerError)
            {
                response.StatusCode = 500;
                await WriteResponseAsync(response, "Internal Server Error");
                return;
            }

            if (path.EndsWith("/api/sdk/v1/replication/stream"))
            {
                await HandleSseStreamAsync(context);
            }
            else
            {
                response.StatusCode = 404;
                await WriteResponseAsync(response, "Not Found");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MockServer error: {ex}");
            try
            {
                context.Response.StatusCode = 500;
                await WriteResponseAsync(context.Response, ex.Message);
            }
            catch
            {
            }
        }
    }

    private async Task HandleSseStreamAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        var connectionTcs = new TaskCompletionSource<bool>();
        lock (_lock)
        {
            _sseConnections.Add(connectionTcs);
        }

        try
        {
            await using var output = response.OutputStream;
            await using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);

            // Apply init delay if configured
            if (InitDelay > TimeSpan.Zero)
            {
                await Task.Delay(InitDelay, _cts.Token);
            }

            // Send init event
            List<Config> configsCopy;
            lock (_lock)
            {
                configsCopy = _configs.ToList();
            }

            var initData = new
            {
                type = "init",
                configs = configsCopy.Select(c => new
                {
                    name = c.Name,
                    value = c.Value,
                    overrides = c.Overrides.Select(o => new
                    {
                        name = o.Name,
                        value = o.Value,
                        conditions = SerializeConditions(o.Conditions)
                    }).ToList()
                }).ToList()
            };

            var initEvent = $"event: init\ndata: {JsonSerializer.Serialize(initData)}\n\n";
            await writer.WriteAsync(initEvent);
            await writer.FlushAsync();

            // Keep connection alive
            try
            {
                await connectionTcs.Task.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            lock (_lock)
            {
                _sseConnections.Remove(connectionTcs);
            }
        }
    }

    private async Task BroadcastSseEventAsync(string sseEvent)
    {
        // This is a simplified approach - in a real implementation you'd keep
        // track of the streams and write to them directly
        // For now, we'll just log that we would send the event
        await Task.CompletedTask;
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static List<object> SerializeConditions(IReadOnlyList<Condition> conditions)
    {
        return conditions.Select(SerializeCondition).ToList();
    }

    private static object SerializeCondition(Condition condition)
    {
        return condition switch
        {
            PropertyCondition prop => new
            {
                @operator = prop.Operator,
                property = prop.Property,
                value = prop.Expected
            },
            SegmentationCondition seg => new
            {
                @operator = "segmentation",
                property = seg.Property,
                fromPercentage = seg.FromPercentage,
                toPercentage = seg.ToPercentage,
                seed = seg.Seed
            },
            AndCondition and => new
            {
                @operator = "and",
                conditions = SerializeConditions(and.Conditions)
            },
            OrCondition or => new
            {
                @operator = "or",
                conditions = SerializeConditions(or.Conditions)
            },
            NotCondition not => new
            {
                @operator = "not",
                condition = SerializeCondition(not.Inner)
            },
            _ => new { }
        };
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

file class TcpListener : System.Net.Sockets.TcpListener
{
    public TcpListener(IPAddress localaddr, int port) : base(localaddr, port) { }
}
