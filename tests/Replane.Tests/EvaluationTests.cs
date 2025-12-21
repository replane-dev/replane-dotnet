namespace Replane.Tests;

public class EvaluationTests
{
    // Helper to convert object to JsonElement for tests
    private static System.Text.Json.JsonElement ToJson(object? value) => JsonValueConverter.ToJsonElement(value);

    [Fact]
    public void EvaluateConfig_NoOverrides_ReturnsBaseValue()
    {
        var config = new Config
        {
            Name = "test-config",
            Value = ToJson("base-value")
        };

        var result = Evaluator.EvaluateConfig(config);

        result.Should().BeOfType<System.Text.Json.JsonElement>();
        JsonValueConverter.Convert<string>((System.Text.Json.JsonElement)result!).Should().Be("base-value");
    }

    [Fact]
    public void EvaluateConfig_MatchingOverride_ReturnsOverrideValue()
    {
        var config = new Config
        {
            Name = "test-config",
            Value = ToJson(false),
            Overrides =
            [
                new Override
                {
                    Name = "beta-users",
                    Conditions =
                    [
                        new PropertyCondition
                        {
                            Op = "equals",
                            Property = "plan",
                            Expected = "beta"
                        }
                    ],
                    Value = ToJson(true)
                }
            ]
        };

        var context = new ReplaneContext { ["plan"] = "beta" };
        var result = Evaluator.EvaluateConfig(config, context);

        JsonValueConverter.Convert<bool>((System.Text.Json.JsonElement)result!).Should().BeTrue();
    }

    [Fact]
    public void EvaluateConfig_NonMatchingOverride_ReturnsBaseValue()
    {
        var config = new Config
        {
            Name = "test-config",
            Value = ToJson(false),
            Overrides =
            [
                new Override
                {
                    Name = "beta-users",
                    Conditions =
                    [
                        new PropertyCondition
                        {
                            Op = "equals",
                            Property = "plan",
                            Expected = "beta"
                        }
                    ],
                    Value = ToJson(true)
                }
            ]
        };

        var context = new ReplaneContext { ["plan"] = "free" };
        var result = Evaluator.EvaluateConfig(config, context);

        JsonValueConverter.Convert<bool>((System.Text.Json.JsonElement)result!).Should().BeFalse();
    }

    [Fact]
    public void EvaluateConfig_FirstMatchingOverrideWins()
    {
        var config = new Config
        {
            Name = "test-config",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "first",
                    Conditions =
                    [
                        new PropertyCondition { Op = "equals", Property = "x", Expected = 1 }
                    ],
                    Value = ToJson("first-value")
                },
                new Override
                {
                    Name = "second",
                    Conditions =
                    [
                        new PropertyCondition { Op = "equals", Property = "x", Expected = 1 }
                    ],
                    Value = ToJson("second-value")
                }
            ]
        };

        var context = new ReplaneContext { ["x"] = 1 };
        var result = Evaluator.EvaluateConfig(config, context);

        JsonValueConverter.Convert<string>((System.Text.Json.JsonElement)result!).Should().Be("first-value");
    }

    // Property condition tests

    [Theory]
    [InlineData("hello", "hello", true)]
    [InlineData("hello", "world", false)]
    [InlineData(123, 123, true)]
    [InlineData(123, 456, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void PropertyCondition_Equals(object contextValue, object expected, bool shouldMatch)
    {
        var condition = new PropertyCondition
        {
            Op = "equals",
            Property = "prop",
            Expected = expected
        };
        var context = new ReplaneContext { ["prop"] = contextValue };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(shouldMatch ? ConditionResult.Matched : ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_In_Matches()
    {
        var condition = new PropertyCondition
        {
            Op = "in",
            Property = "region",
            Expected = new List<object?> { "us-east", "us-west", "eu-west" }
        };
        var context = new ReplaneContext { ["region"] = "us-east" };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void PropertyCondition_In_NotMatches()
    {
        var condition = new PropertyCondition
        {
            Op = "in",
            Property = "region",
            Expected = new List<object?> { "us-east", "us-west", "eu-west" }
        };
        var context = new ReplaneContext { ["region"] = "ap-south" };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_NotIn_Matches()
    {
        var condition = new PropertyCondition
        {
            Op = "not_in",
            Property = "region",
            Expected = new List<object?> { "blocked-region" }
        };
        var context = new ReplaneContext { ["region"] = "allowed-region" };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void PropertyCondition_LessThan()
    {
        var condition = new PropertyCondition
        {
            Op = "less_than",
            Property = "age",
            Expected = 18
        };

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 15 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 18 })
            .Should().Be(ConditionResult.NotMatched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 25 })
            .Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_LessThanOrEqual()
    {
        var condition = new PropertyCondition
        {
            Op = "less_than_or_equal",
            Property = "age",
            Expected = 18
        };

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 15 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 18 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["age"] = 25 })
            .Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_GreaterThan()
    {
        var condition = new PropertyCondition
        {
            Op = "greater_than",
            Property = "score",
            Expected = 100
        };

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["score"] = 150 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["score"] = 100 })
            .Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_GreaterThanOrEqual()
    {
        var condition = new PropertyCondition
        {
            Op = "greater_than_or_equal",
            Property = "score",
            Expected = 100
        };

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["score"] = 150 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["score"] = 100 })
            .Should().Be(ConditionResult.Matched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["score"] = 50 })
            .Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void PropertyCondition_MissingProperty_ReturnsUnknown()
    {
        var condition = new PropertyCondition
        {
            Op = "equals",
            Property = "missing",
            Expected = "value"
        };
        var context = new ReplaneContext();

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Unknown);
    }

    // Logical condition tests

    [Fact]
    public void AndCondition_AllMatch_ReturnsMatched()
    {
        var condition = new AndCondition
        {
            Conditions =
            [
                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                new PropertyCondition { Op = "equals", Property = "b", Expected = 2 }
            ]
        };
        var context = new ReplaneContext { ["a"] = 1, ["b"] = 2 };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void AndCondition_OneNotMatch_ReturnsNotMatched()
    {
        var condition = new AndCondition
        {
            Conditions =
            [
                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                new PropertyCondition { Op = "equals", Property = "b", Expected = 2 }
            ]
        };
        var context = new ReplaneContext { ["a"] = 1, ["b"] = 999 };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void AndCondition_OneUnknown_ReturnsUnknown()
    {
        var condition = new AndCondition
        {
            Conditions =
            [
                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                new PropertyCondition { Op = "equals", Property = "missing", Expected = 2 }
            ]
        };
        var context = new ReplaneContext { ["a"] = 1 };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Unknown);
    }

    [Fact]
    public void OrCondition_OneMatch_ReturnsMatched()
    {
        var condition = new OrCondition
        {
            Conditions =
            [
                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                new PropertyCondition { Op = "equals", Property = "b", Expected = 2 }
            ]
        };
        var context = new ReplaneContext { ["a"] = 1, ["b"] = 999 };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void OrCondition_NoneMatch_ReturnsNotMatched()
    {
        var condition = new OrCondition
        {
            Conditions =
            [
                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                new PropertyCondition { Op = "equals", Property = "b", Expected = 2 }
            ]
        };
        var context = new ReplaneContext { ["a"] = 999, ["b"] = 999 };

        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.NotMatched);
    }

    [Fact]
    public void NotCondition_InvertsMatched()
    {
        var condition = new NotCondition
        {
            Inner = new PropertyCondition { Op = "equals", Property = "a", Expected = 1 }
        };

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["a"] = 1 })
            .Should().Be(ConditionResult.NotMatched);

        Evaluator.EvaluateCondition(condition, new ReplaneContext { ["a"] = 2 })
            .Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void NotCondition_UnknownRemainsUnknown()
    {
        var condition = new NotCondition
        {
            Inner = new PropertyCondition { Op = "equals", Property = "missing", Expected = 1 }
        };

        var result = Evaluator.EvaluateCondition(condition, new ReplaneContext());

        result.Should().Be(ConditionResult.Unknown);
    }

    // Segmentation condition tests

    [Fact]
    public void SegmentationCondition_InRange_ReturnsMatched()
    {
        // Find a user that falls in a known percentage range
        var seed = "test-seed";
        string? matchingUser = null;

        for (var i = 0; i < 1000; i++)
        {
            var userId = $"user-{i}";
            var percentage = Fnv1a.HashToPercentage(userId, seed);
            if (percentage >= 0 && percentage < 50)
            {
                matchingUser = userId;
                break;
            }
        }

        matchingUser.Should().NotBeNull();

        var condition = new SegmentationCondition
        {
            Property = "user_id",
            FromPercentage = 0,
            ToPercentage = 50,
            Seed = seed
        };

        var context = new ReplaneContext { ["user_id"] = matchingUser };
        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }

    [Fact]
    public void SegmentationCondition_MissingProperty_ReturnsUnknown()
    {
        var condition = new SegmentationCondition
        {
            Property = "user_id",
            FromPercentage = 0,
            ToPercentage = 50,
            Seed = "seed"
        };

        var result = Evaluator.EvaluateCondition(condition, new ReplaneContext());

        result.Should().Be(ConditionResult.Unknown);
    }

    [Fact]
    public void SegmentationCondition_Deterministic()
    {
        var condition = new SegmentationCondition
        {
            Property = "user_id",
            FromPercentage = 0,
            ToPercentage = 10,
            Seed = "feature-1"
        };

        var context = new ReplaneContext { ["user_id"] = "test-user-123" };

        var result1 = Evaluator.EvaluateCondition(condition, context);
        var result2 = Evaluator.EvaluateCondition(condition, context);

        result1.Should().Be(result2);
    }

    // Type coercion tests

    [Fact]
    public void TypeCoercion_NumberComparison()
    {
        var condition = new PropertyCondition
        {
            Op = "equals",
            Property = "count",
            Expected = 42L // JSON typically parses as long
        };

        // Should match even if context value is int
        var context = new ReplaneContext { ["count"] = 42 };
        var result = Evaluator.EvaluateCondition(condition, context);

        result.Should().Be(ConditionResult.Matched);
    }
}
