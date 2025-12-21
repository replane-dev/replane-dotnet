using System.Globalization;

namespace Replane;

/// <summary>
/// Result of evaluating a condition.
/// </summary>
public enum ConditionResult
{
    /// <summary>Condition matched.</summary>
    Matched,

    /// <summary>Condition did not match.</summary>
    NotMatched,

    /// <summary>Evaluation is indeterminate (e.g., missing context property).</summary>
    Unknown
}

/// <summary>
/// Override evaluation logic for client-side config evaluation.
/// Context never leaves the application - all evaluation happens locally.
/// </summary>
public static class Evaluator
{
    /// <summary>
    /// Evaluate a config and return the appropriate value.
    /// Overrides are evaluated in order. The first matching override's value is returned.
    /// If no overrides match, the base value is returned.
    /// </summary>
    public static object? EvaluateConfig(Config config, ReplaneContext? context = null)
    {
        var (result, _) = EvaluateConfigWithDetails(config, context, null);
        return result;
    }

    /// <summary>
    /// Evaluate a config with detailed logging. Returns the value and the index of the matched override (-1 if none).
    /// </summary>
    public static (object? Value, int MatchedOverrideIndex) EvaluateConfigWithDetails(
        Config config,
        ReplaneContext? context,
        IReplaneLogger? logger)
    {
        context ??= [];

        for (var i = 0; i < config.Overrides.Count; i++)
        {
            var @override = config.Overrides[i];
            logger?.LogDebug($"    Evaluating override #{i} (conditions: {string.Join(", ", @override.Conditions.Select(FormatCondition))})");

            var matched = EvaluateOverrideWithLogging(@override, context, logger, i);
            if (matched)
            {
                logger?.LogDebug($"    Override #{i} MATCHED");
                return (@override.Value, i);
            }
            else
            {
                logger?.LogDebug($"    Override #{i} did not match");
            }
        }

        return (config.Value, -1);
    }

    private static string FormatCondition(Condition condition)
    {
        return condition switch
        {
            PropertyCondition prop => $"property({prop.Property} {prop.Operator} {prop.Expected})",
            SegmentationCondition seg => $"segment({seg.Property} in {seg.FromPercentage}-{seg.ToPercentage}%)",
            AndCondition and => $"AND({and.Conditions.Count})",
            OrCondition or => $"OR({or.Conditions.Count})",
            NotCondition => "NOT(...)",
            _ => condition.GetType().Name
        };
    }

    private static bool EvaluateOverrideWithLogging(Override @override, ReplaneContext context, IReplaneLogger? logger, int overrideIndex)
    {
        foreach (var condition in @override.Conditions)
        {
            var result = EvaluateConditionWithLogging(condition, context, logger);
            if (result != ConditionResult.Matched)
            {
                return false;
            }
        }
        return true;
    }

    private static ConditionResult EvaluateConditionWithLogging(Condition condition, ReplaneContext context, IReplaneLogger? logger)
    {
        var result = EvaluateCondition(condition, context);

        if (logger != null)
        {
            var conditionDesc = FormatConditionForLogging(condition, context);
            logger.LogDebug($"      Condition: {conditionDesc} => {result}");
        }

        return result;
    }

    private static string FormatConditionForLogging(Condition condition, ReplaneContext context)
    {
        switch (condition)
        {
            case PropertyCondition prop:
                context.TryGetValue(prop.Property, out var propCtxValue);
                return $"property \"{prop.Property}\" ({FormatContextValue(propCtxValue)}) {prop.Operator} {FormatExpectedValue(prop.Expected)}";

            case SegmentationCondition seg:
                context.TryGetValue(seg.Property, out var segCtxValue);
                var strValue = segCtxValue != null ? Convert.ToString(segCtxValue, CultureInfo.InvariantCulture) ?? "" : "";
                var percentage = segCtxValue != null ? Fnv1a.HashToPercentage(strValue, seg.Seed) : -1;
                return $"segment \"{seg.Property}\" ({FormatContextValue(segCtxValue)}) hash={percentage:F2}% in [{seg.FromPercentage}-{seg.ToPercentage}%)";

            case AndCondition and:
                return $"AND condition with {and.Conditions.Count} sub-conditions";

            case OrCondition or:
                return $"OR condition with {or.Conditions.Count} sub-conditions";

            case NotCondition:
                return "NOT condition";

            default:
                return condition.GetType().Name;
        }
    }

    private static string FormatContextValue(object? value)
    {
        return value switch
        {
            null => "<missing>",
            string s => $"\"{s}\"",
            _ => value.ToString() ?? "null"
        };
    }

    private static string FormatExpectedValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            IEnumerable<object?> list => $"[{string.Join(", ", list.Select(FormatExpectedValue))}]",
            _ => value.ToString() ?? "null"
        };
    }

    /// <summary>
    /// Check if an override matches the given context.
    /// All conditions in the override must match for it to apply.
    /// </summary>
    public static bool EvaluateOverride(Override @override, ReplaneContext context)
    {
        foreach (var condition in @override.Conditions)
        {
            var result = EvaluateCondition(condition, context);
            if (result != ConditionResult.Matched)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Evaluate a single condition against a context.
    /// </summary>
    public static ConditionResult EvaluateCondition(Condition condition, ReplaneContext context)
    {
        return condition switch
        {
            AndCondition and => EvaluateAnd(and, context),
            OrCondition or => EvaluateOr(or, context),
            NotCondition not => EvaluateNot(not, context),
            SegmentationCondition seg => EvaluateSegmentation(seg, context),
            PropertyCondition prop => EvaluateProperty(prop, context),
            _ => ConditionResult.Unknown
        };
    }

    private static ConditionResult EvaluateAnd(AndCondition condition, ReplaneContext context)
    {
        var hasUnknown = false;
        foreach (var c in condition.Conditions)
        {
            var result = EvaluateCondition(c, context);
            if (result == ConditionResult.NotMatched)
            {
                return ConditionResult.NotMatched;
            }
            if (result == ConditionResult.Unknown)
            {
                hasUnknown = true;
            }
        }
        return hasUnknown ? ConditionResult.Unknown : ConditionResult.Matched;
    }

    private static ConditionResult EvaluateOr(OrCondition condition, ReplaneContext context)
    {
        var hasUnknown = false;
        foreach (var c in condition.Conditions)
        {
            var result = EvaluateCondition(c, context);
            if (result == ConditionResult.Matched)
            {
                return ConditionResult.Matched;
            }
            if (result == ConditionResult.Unknown)
            {
                hasUnknown = true;
            }
        }
        return hasUnknown ? ConditionResult.Unknown : ConditionResult.NotMatched;
    }

    private static ConditionResult EvaluateNot(NotCondition condition, ReplaneContext context)
    {
        var result = EvaluateCondition(condition.Inner, context);
        return result switch
        {
            ConditionResult.Matched => ConditionResult.NotMatched,
            ConditionResult.NotMatched => ConditionResult.Matched,
            _ => ConditionResult.Unknown
        };
    }

    private static ConditionResult EvaluateSegmentation(SegmentationCondition condition, ReplaneContext context)
    {
        if (!context.TryGetValue(condition.Property, out var ctxValue) || ctxValue == null)
        {
            return ConditionResult.Unknown;
        }

        var strValue = Convert.ToString(ctxValue, CultureInfo.InvariantCulture) ?? "";
        var percentage = Fnv1a.HashToPercentage(strValue, condition.Seed);

        if (percentage >= condition.FromPercentage && percentage < condition.ToPercentage)
        {
            return ConditionResult.Matched;
        }
        return ConditionResult.NotMatched;
    }

    private static ConditionResult EvaluateProperty(PropertyCondition condition, ReplaneContext context)
    {
        if (!context.TryGetValue(condition.Property, out var ctxValue))
        {
            return ConditionResult.Unknown;
        }

        return condition.Operator switch
        {
            "equals" => CompareValues(ctxValue, condition.Expected, "equals"),
            "in" => EvaluateIn(ctxValue, condition.Expected),
            "not_in" => EvaluateNotIn(ctxValue, condition.Expected),
            "less_than" => CompareValues(ctxValue, condition.Expected, "less_than"),
            "less_than_or_equal" => CompareValues(ctxValue, condition.Expected, "less_than_or_equal"),
            "greater_than" => CompareValues(ctxValue, condition.Expected, "greater_than"),
            "greater_than_or_equal" => CompareValues(ctxValue, condition.Expected, "greater_than_or_equal"),
            _ => ConditionResult.Unknown
        };
    }

    private static ConditionResult EvaluateIn(object? ctxValue, object? expected)
    {
        if (ctxValue == null)
        {
            return ConditionResult.Unknown;
        }

        if (expected is not IEnumerable<object?> list)
        {
            return ConditionResult.Unknown;
        }

        foreach (var item in list)
        {
            var casted = CastToType(item, ctxValue.GetType());
            if (ValuesEqual(ctxValue, casted))
            {
                return ConditionResult.Matched;
            }
        }
        return ConditionResult.NotMatched;
    }

    private static ConditionResult EvaluateNotIn(object? ctxValue, object? expected)
    {
        if (ctxValue == null)
        {
            return ConditionResult.Unknown;
        }

        if (expected is not IEnumerable<object?> list)
        {
            return ConditionResult.Unknown;
        }

        foreach (var item in list)
        {
            var casted = CastToType(item, ctxValue.GetType());
            if (ValuesEqual(ctxValue, casted))
            {
                return ConditionResult.NotMatched;
            }
        }
        return ConditionResult.Matched;
    }

    private static ConditionResult CompareValues(object? ctxValue, object? expected, string op)
    {
        if (ctxValue == null)
        {
            return ConditionResult.Unknown;
        }

        var casted = CastToType(expected, ctxValue.GetType());

        return op switch
        {
            "equals" => ValuesEqual(ctxValue, casted) ? ConditionResult.Matched : ConditionResult.NotMatched,
            "less_than" => CompareLess(ctxValue, casted, false),
            "less_than_or_equal" => CompareLess(ctxValue, casted, true),
            "greater_than" => CompareGreater(ctxValue, casted, false),
            "greater_than_or_equal" => CompareGreater(ctxValue, casted, true),
            _ => ConditionResult.Unknown
        };
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Handle numeric comparisons
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) == ToDouble(b);
        }

        return a.Equals(b);
    }

    private static ConditionResult CompareLess(object? ctxValue, object? expected, bool orEqual)
    {
        if (ctxValue == null || expected == null)
        {
            return ConditionResult.Unknown;
        }

        try
        {
            if (IsNumeric(ctxValue) && IsNumeric(expected))
            {
                var a = ToDouble(ctxValue);
                var b = ToDouble(expected);
                return (orEqual ? a <= b : a < b) ? ConditionResult.Matched : ConditionResult.NotMatched;
            }

            if (ctxValue is IComparable comparable)
            {
                var cmp = comparable.CompareTo(expected);
                return (orEqual ? cmp <= 0 : cmp < 0) ? ConditionResult.Matched : ConditionResult.NotMatched;
            }
        }
        catch
        {
            return ConditionResult.Unknown;
        }

        return ConditionResult.Unknown;
    }

    private static ConditionResult CompareGreater(object? ctxValue, object? expected, bool orEqual)
    {
        if (ctxValue == null || expected == null)
        {
            return ConditionResult.Unknown;
        }

        try
        {
            if (IsNumeric(ctxValue) && IsNumeric(expected))
            {
                var a = ToDouble(ctxValue);
                var b = ToDouble(expected);
                return (orEqual ? a >= b : a > b) ? ConditionResult.Matched : ConditionResult.NotMatched;
            }

            if (ctxValue is IComparable comparable)
            {
                var cmp = comparable.CompareTo(expected);
                return (orEqual ? cmp >= 0 : cmp > 0) ? ConditionResult.Matched : ConditionResult.NotMatched;
            }
        }
        catch
        {
            return ConditionResult.Unknown;
        }

        return ConditionResult.Unknown;
    }

    private static object? CastToType(object? value, Type targetType)
    {
        if (value == null) return null;
        if (value.GetType() == targetType) return value;

        try
        {
            if (targetType == typeof(bool))
            {
                if (value is string s)
                {
                    return s.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || s == "1"
                        || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }
                return Convert.ToBoolean(value);
            }

            if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(double) || targetType == typeof(float))
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(string))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Fall through to return original value
        }

        return value;
    }

    private static bool IsNumeric(object? value)
    {
        return value is int or long or float or double or decimal or short or byte or sbyte or ushort or uint or ulong;
    }

    private static double ToDouble(object? value)
    {
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}
