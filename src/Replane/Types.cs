using System.Text.Json;
using System.Text.Json.Serialization;

namespace Replane;

/// <summary>
/// Runtime context for override evaluation.
/// Context is a dictionary of string keys to primitive values.
/// </summary>
public class ReplaneContext : Dictionary<string, object?>
{
    public ReplaneContext() : base(StringComparer.Ordinal) { }

    public ReplaneContext(IDictionary<string, object?> dictionary) : base(dictionary, StringComparer.Ordinal) { }

    /// <summary>
    /// Merge this context with another, with the other context taking precedence.
    /// </summary>
    public ReplaneContext Merge(ReplaneContext? other)
    {
        if (other == null || other.Count == 0)
            return this;

        var merged = new ReplaneContext(this);
        foreach (var (key, value) in other)
        {
            merged[key] = value;
        }
        return merged;
    }
}

/// <summary>
/// A configuration with its base value and override rules.
/// </summary>
public sealed record Config
{
    /// <summary>Unique identifier for this config.</summary>
    public required string Name { get; init; }

    /// <summary>The base/default value stored as a raw JsonElement for lazy deserialization.</summary>
    public required JsonElement Value { get; init; }

    /// <summary>List of override rules evaluated in order.</summary>
    public IReadOnlyList<Override> Overrides { get; init; } = [];

    /// <summary>
    /// Gets the value deserialized to the specified type.
    /// </summary>
    public T? GetValue<T>() => JsonValueConverter.Convert<T>(Value);
}

/// <summary>
/// An override rule that returns a specific value when conditions match.
/// </summary>
public sealed record Override
{
    /// <summary>Name of the override rule.</summary>
    public required string Name { get; init; }

    /// <summary>Conditions that must all match for this override to apply.</summary>
    public required IReadOnlyList<Condition> Conditions { get; init; }

    /// <summary>Value to return when this override matches stored as a raw JsonElement.</summary>
    public required JsonElement Value { get; init; }

    /// <summary>
    /// Gets the value deserialized to the specified type.
    /// </summary>
    public T? GetValue<T>() => JsonValueConverter.Convert<T>(Value);
}

/// <summary>
/// Base class for all condition types.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "operator")]
[JsonDerivedType(typeof(PropertyCondition), "equals")]
[JsonDerivedType(typeof(PropertyCondition), "in")]
[JsonDerivedType(typeof(PropertyCondition), "not_in")]
[JsonDerivedType(typeof(PropertyCondition), "less_than")]
[JsonDerivedType(typeof(PropertyCondition), "less_than_or_equal")]
[JsonDerivedType(typeof(PropertyCondition), "greater_than")]
[JsonDerivedType(typeof(PropertyCondition), "greater_than_or_equal")]
[JsonDerivedType(typeof(SegmentationCondition), "segmentation")]
[JsonDerivedType(typeof(AndCondition), "and")]
[JsonDerivedType(typeof(OrCondition), "or")]
[JsonDerivedType(typeof(NotCondition), "not")]
public abstract record Condition
{
    public abstract string Operator { get; }
}

/// <summary>
/// A condition that compares a context property against expected values.
/// </summary>
public sealed record PropertyCondition : Condition
{
    private readonly string _operator = "equals";
    public override string Operator => _operator;

    /// <summary>The comparison operator.</summary>
    public string Op { get => _operator; init => _operator = value; }

    /// <summary>The context property to compare.</summary>
    public required string Property { get; init; }

    /// <summary>The expected value(s) to compare against.</summary>
    public required object? Expected { get; init; }
}

/// <summary>
/// A condition for percentage-based bucketing (gradual rollouts).
/// </summary>
public sealed record SegmentationCondition : Condition
{
    public override string Operator => "segmentation";

    /// <summary>The context property to use for bucketing.</summary>
    public required string Property { get; init; }

    /// <summary>Start of the percentage range (inclusive).</summary>
    public required double FromPercentage { get; init; }

    /// <summary>End of the percentage range (exclusive).</summary>
    public required double ToPercentage { get; init; }

    /// <summary>Salt to ensure different configs get different distributions.</summary>
    public required string Seed { get; init; }
}

/// <summary>
/// Logical AND of multiple conditions.
/// </summary>
public sealed record AndCondition : Condition
{
    public override string Operator => "and";

    /// <summary>All conditions that must match.</summary>
    public required IReadOnlyList<Condition> Conditions { get; init; }
}

/// <summary>
/// Logical OR of multiple conditions.
/// </summary>
public sealed record OrCondition : Condition
{
    public override string Operator => "or";

    /// <summary>At least one condition must match.</summary>
    public required IReadOnlyList<Condition> Conditions { get; init; }
}

/// <summary>
/// Logical NOT of a condition.
/// </summary>
public sealed record NotCondition : Condition
{
    public override string Operator => "not";

    /// <summary>The condition to negate.</summary>
    [JsonPropertyName("condition")]
    public required Condition Inner { get; init; }
}

/// <summary>
/// Utility class for converting JsonElement to typed values.
/// </summary>
public static class JsonValueConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Converts a JsonElement to the specified type.
    /// </summary>
    public static T? Convert<T>(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return default;
        }

        // If T is JsonElement, return as-is
        if (typeof(T) == typeof(JsonElement))
        {
            return (T)(object)element;
        }

        // If T is object, return the parsed primitive value for simple types
        if (typeof(T) == typeof(object))
        {
            return (T?)ParseJsonValueAsObject(element);
        }

        // For all other types, deserialize using System.Text.Json
        return element.Deserialize<T>(JsonOptions);
    }

    /// <summary>
    /// Parses a JsonElement to a primitive .NET object (for condition evaluation).
    /// </summary>
    public static object? ParseJsonValueAsObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ParseJsonValueAsObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ParseJsonValueAsObject(p.Value)),
            _ => null
        };
    }

    /// <summary>
    /// Creates a JsonElement from a .NET object (for testing/defaults).
    /// </summary>
    public static JsonElement ToJsonElement(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

/// <summary>
/// Parser for config data from API responses.
/// </summary>
public static class ConfigParser
{
    public static Condition ParseCondition(JsonElement element)
    {
        var op = element.GetProperty("operator").GetString() ?? throw new JsonException("Missing operator");

        return op switch
        {
            "and" => new AndCondition
            {
                Conditions = element.GetProperty("conditions").EnumerateArray()
                    .Select(ParseCondition).ToList()
            },
            "or" => new OrCondition
            {
                Conditions = element.GetProperty("conditions").EnumerateArray()
                    .Select(ParseCondition).ToList()
            },
            "not" => new NotCondition
            {
                Inner = ParseCondition(element.GetProperty("condition"))
            },
            "segmentation" => new SegmentationCondition
            {
                Property = element.GetProperty("property").GetString()!,
                FromPercentage = element.GetProperty("fromPercentage").GetDouble(),
                ToPercentage = element.GetProperty("toPercentage").GetDouble(),
                Seed = element.GetProperty("seed").GetString()!
            },
            "equals" or "in" or "not_in" or "less_than" or "less_than_or_equal"
                or "greater_than" or "greater_than_or_equal" => new PropertyCondition
            {
                Op = op,
                Property = element.GetProperty("property").GetString()!,
                Expected = GetExpectedValue(element)
            },
            _ => throw new JsonException($"Unknown condition operator: {op}")
        };
    }

    private static object? GetExpectedValue(JsonElement element)
    {
        // Try 'expected' first (Python SDK style), then 'value' (JS SDK style)
        // For condition evaluation, we need the actual parsed value
        if (element.TryGetProperty("expected", out var expected))
            return JsonValueConverter.ParseJsonValueAsObject(expected);
        if (element.TryGetProperty("value", out var value))
            return JsonValueConverter.ParseJsonValueAsObject(value);
        return null;
    }

    public static Override ParseOverride(JsonElement element)
    {
        return new Override
        {
            Name = element.GetProperty("name").GetString()!,
            Conditions = element.GetProperty("conditions").EnumerateArray()
                .Select(ParseCondition).ToList(),
            Value = element.GetProperty("value").Clone()
        };
    }

    public static Config ParseConfig(JsonElement element)
    {
        var overrides = new List<Override>();
        if (element.TryGetProperty("overrides", out var overridesElement))
        {
            overrides = overridesElement.EnumerateArray()
                .Select(ParseOverride).ToList();
        }

        return new Config
        {
            Name = element.GetProperty("name").GetString()!,
            Value = element.GetProperty("value").Clone(),
            Overrides = overrides
        };
    }
}
