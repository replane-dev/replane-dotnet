using System.Text.Json;

namespace Replane.Testing;

/// <summary>
/// An in-memory Replane client for testing.
/// Implements IReplaneClient so it can be used as a drop-in replacement for ReplaneClient.
/// Useful for unit tests where you don't want to connect to a real Replane server.
/// </summary>
public sealed class InMemoryReplaneClient : IReplaneClient
{
    private readonly Dictionary<string, Config> _configs = new(StringComparer.Ordinal);
    private readonly ReplaneContext _context;
    private bool _closed;

    /// <summary>
    /// Event raised when any config is changed.
    /// </summary>
    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    /// Creates a new in-memory client.
    /// </summary>
    /// <param name="initialConfigs">Optional dictionary of config name -> value.</param>
    /// <param name="context">Default context for override evaluation.</param>
    public InMemoryReplaneClient(
        Dictionary<string, object?>? initialConfigs = null,
        ReplaneContext? context = null)
    {
        _context = context ?? [];

        if (initialConfigs != null)
        {
            foreach (var (name, value) in initialConfigs)
            {
                var jsonValue = JsonValueConverter.ToJsonElement(value);
                _configs[name] = new Config { Name = name, Value = jsonValue };
            }
        }
    }

    /// <summary>
    /// Get a config value.
    /// </summary>
    public T? Get<T>(string name, ReplaneContext? context = null, T? defaultValue = default)
    {
        if (_closed)
        {
            throw new ClientClosedException();
        }

        var mergedContext = _context.Merge(context);

        if (!_configs.TryGetValue(name, out var config))
        {
            if (defaultValue is not null || typeof(T).IsValueType)
            {
                return defaultValue;
            }
            throw new ConfigNotFoundException(name);
        }

        var result = Evaluator.EvaluateConfig(config, mergedContext);

        // Result is a JsonElement, deserialize to requested type
        if (result is JsonElement element)
        {
            return JsonValueConverter.Convert<T>(element);
        }

        return ConvertValue<T>(result);
    }

    /// <summary>
    /// Get a config value as object.
    /// </summary>
    public object? Get(string name, ReplaneContext? context = null, object? defaultValue = null)
    {
        return Get<object>(name, context, defaultValue);
    }

    /// <summary>
    /// Set a config value (simple form without overrides).
    /// </summary>
    public void Set(string name, object? value)
    {
        SetConfig(name, value);
    }

    /// <summary>
    /// Set a config with optional overrides.
    /// </summary>
    public void SetConfig(string name, object? value, IReadOnlyList<Override>? overrides = null)
    {
        var jsonValue = JsonValueConverter.ToJsonElement(value);
        var config = new Config
        {
            Name = name,
            Value = jsonValue,
            Overrides = overrides ?? []
        };

        _configs[name] = config;
        OnConfigChanged(config);
    }

    /// <summary>
    /// Set a config from raw data (for testing with JSON-like structures).
    /// </summary>
    public void SetConfigWithOverrides(
        string name,
        object? value,
        List<OverrideData>? overrides = null)
    {
        var parsedOverrides = new List<Override>();

        if (overrides != null)
        {
            foreach (var overrideData in overrides)
            {
                var conditions = overrideData.Conditions
                    .Select(ParseConditionData)
                    .ToList();

                parsedOverrides.Add(new Override
                {
                    Name = overrideData.Name,
                    Conditions = conditions,
                    Value = JsonValueConverter.ToJsonElement(overrideData.Value)
                });
            }
        }

        SetConfig(name, value, parsedOverrides);
    }

    /// <summary>
    /// Delete a config.
    /// </summary>
    public bool Delete(string name)
    {
        return _configs.Remove(name);
    }

    /// <summary>
    /// Check if the client has finished initialization.
    /// For in-memory client, always returns true.
    /// </summary>
    public bool IsInitialized => true;

    /// <summary>
    /// Close the client.
    /// </summary>
    public void Close()
    {
        _closed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Get all configs (for testing inspection).
    /// </summary>
    public IReadOnlyDictionary<string, Config> Configs =>
        new Dictionary<string, Config>(_configs);

    /// <summary>
    /// Raises the ConfigChanged event.
    /// </summary>
    private void OnConfigChanged(Config config)
    {
        var handler = ConfigChanged;
        if (handler == null) return;

        var args = new ConfigChangedEventArgs
        {
            ConfigName = config.Name,
            Config = config
        };

        try
        {
            handler(this, args);
        }
        catch
        {
            // Ignore errors in callbacks during tests
        }
    }

    private static Condition ParseConditionData(ConditionData data)
    {
        return data.Operator switch
        {
            "and" => new AndCondition
            {
                Conditions = data.Conditions?.Select(ParseConditionData).ToList() ?? []
            },
            "or" => new OrCondition
            {
                Conditions = data.Conditions?.Select(ParseConditionData).ToList() ?? []
            },
            "not" => new NotCondition
            {
                Inner = ParseConditionData(data.Condition!)
            },
            "segmentation" => new SegmentationCondition
            {
                Property = data.Property!,
                FromPercentage = data.FromPercentage ?? 0,
                ToPercentage = data.ToPercentage ?? 100,
                Seed = data.Seed!
            },
            _ => new PropertyCondition
            {
                Op = data.Operator,
                Property = data.Property!,
                Expected = data.Expected
            }
        };
    }

    private static T? ConvertValue<T>(object? value)
    {
        if (value == null) return default;
        if (value is T typed) return typed;
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

/// <summary>
/// Data structure for defining overrides in tests.
/// </summary>
public sealed class OverrideData
{
    public required string Name { get; init; }
    public required List<ConditionData> Conditions { get; init; }
    public required object? Value { get; init; }
}

/// <summary>
/// Data structure for defining conditions in tests.
/// </summary>
public sealed class ConditionData
{
    public required string Operator { get; init; }
    public string? Property { get; init; }
    public object? Expected { get; init; }
    public double? FromPercentage { get; init; }
    public double? ToPercentage { get; init; }
    public string? Seed { get; init; }
    public List<ConditionData>? Conditions { get; init; }
    public ConditionData? Condition { get; init; }
}

/// <summary>
/// Factory for creating test clients.
/// </summary>
public static class TestClient
{
    /// <summary>
    /// Create an in-memory client for testing.
    /// </summary>
    public static InMemoryReplaneClient Create(
        Dictionary<string, object?>? configs = null,
        ReplaneContext? context = null)
    {
        return new InMemoryReplaneClient(configs, context);
    }
}
