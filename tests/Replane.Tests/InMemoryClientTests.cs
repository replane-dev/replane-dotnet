using Replane.Testing;
using System.Text.Json;

namespace Replane.Tests;

// Complex types for testing
public record ThemeConfig
{
    public bool DarkMode { get; init; }
    public string PrimaryColor { get; init; } = "";
    public int FontSize { get; init; }
}

public record FeatureFlags
{
    public bool NewUI { get; init; }
    public bool BetaFeatures { get; init; }
    public List<string> EnabledModules { get; init; } = [];
}

public record ApiConfig
{
    public string Endpoint { get; init; } = "";
    public int TimeoutMs { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
}

public class InMemoryClientTests
{
    [Fact]
    public void Create_WithInitialConfigs()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["feature-enabled"] = true,
            ["max-items"] = 100,
            ["api-key"] = "secret"
        });

        client.Get<bool>("feature-enabled").Should().BeTrue();
        client.Get<int>("max-items").Should().Be(100);
        client.Get<string>("api-key").Should().Be("secret");
    }

    [Fact]
    public void Get_WithDefault_ReturnsDefaultIfNotFound()
    {
        using var client = TestClient.Create();

        var value = client.Get("missing", defaultValue: "default-value");

        value.Should().Be("default-value");
    }

    [Fact]
    public void Get_WithoutDefault_ThrowsIfNotFound()
    {
        using var client = TestClient.Create();

        var act = () => client.Get<string>("missing");

        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public void Set_UpdatesConfig()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["counter"] = 0
        });

        client.Set("counter", 42);

        client.Get<int>("counter").Should().Be(42);
    }

    [Fact]
    public void SetConfig_WithOverrides_EvaluatesCorrectly()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "premium-feature",
            value: false,
            overrides:
            [
                new OverrideData
                {
                    Name = "premium-users",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "equals",
                            Property = "plan",
                            Expected = "premium"
                        }
                    ],
                    Value = true
                }
            ]);

        client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "free" })
            .Should().BeFalse();

        client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "premium" })
            .Should().BeTrue();
    }

    [Fact]
    public void SetConfig_WithInCondition()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "region-feature",
            value: "default",
            overrides:
            [
                new OverrideData
                {
                    Name = "us-regions",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "in",
                            Property = "region",
                            Expected = new List<object?> { "us-east", "us-west" }
                        }
                    ],
                    Value = "us-value"
                }
            ]);

        client.Get<string>("region-feature", new ReplaneContext { ["region"] = "eu-west" })
            .Should().Be("default");

        client.Get<string>("region-feature", new ReplaneContext { ["region"] = "us-east" })
            .Should().Be("us-value");
    }

    [Fact]
    public void SetConfig_WithSegmentation()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "ab-test",
            value: "control",
            overrides:
            [
                new OverrideData
                {
                    Name = "treatment-group",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "segmentation",
                            Property = "user_id",
                            FromPercentage = 0,
                            ToPercentage = 50,
                            Seed = "ab-test-seed"
                        }
                    ],
                    Value = "treatment"
                }
            ]);

        // The result should be deterministic for any given user
        var result1 = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = "test-user" });
        var result2 = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = "test-user" });
        result1.Should().Be(result2);

        // Different users may get different results (but this is deterministic)
        var results = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var result = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = $"user-{i}" });
            results.Add(result!);
        }

        // With 100 users split 50/50, we should see both values
        results.Should().Contain("control");
        results.Should().Contain("treatment");
    }

    [Fact]
    public void DefaultContext_IsUsed()
    {
        using var client = new InMemoryReplaneClient(
            new Dictionary<string, object?> { ["config"] = "default" },
            context: new ReplaneContext { ["plan"] = "premium" });

        client.SetConfigWithOverrides(
            "config",
            value: "default",
            overrides:
            [
                new OverrideData
                {
                    Name = "premium",
                    Conditions =
                    [
                        new ConditionData { Operator = "equals", Property = "plan", Expected = "premium" }
                    ],
                    Value = "premium-value"
                }
            ]);

        // Default context should be used
        client.Get<string>("config").Should().Be("premium-value");
    }

    [Fact]
    public void ConfigChanged_ReceivesAllUpdates()
    {
        using var client = TestClient.Create();

        var updates = new List<(string Name, Config Config)>();
        client.ConfigChanged += (sender, e) => updates.Add((e.ConfigName, e.Config));

        client.Set("config-1", "value-1");
        client.Set("config-2", "value-2");

        updates.Should().HaveCount(2);
        updates[0].Name.Should().Be("config-1");
        updates[1].Name.Should().Be("config-2");
    }

    [Fact]
    public void ConfigChanged_CanUnsubscribe()
    {
        using var client = TestClient.Create();

        var updates = new List<(string Name, Config Config)>();
        void Handler(object? sender, ConfigChangedEventArgs e) => updates.Add((e.ConfigName, e.Config));

        client.ConfigChanged += Handler;

        client.Set("config-1", "value-1");
        updates.Should().HaveCount(1);

        client.ConfigChanged -= Handler;

        client.Set("config-2", "value-2");
        updates.Should().HaveCount(1); // No more updates after unsubscribe
    }

    [Fact]
    public void ConfigChanged_CanFilterByConfigName()
    {
        using var client = TestClient.Create();

        var updates = new List<Config>();
        client.ConfigChanged += (sender, e) =>
        {
            if (e.ConfigName == "watched")
            {
                updates.Add(e.Config);
            }
        };

        client.Set("watched", "v1");
        client.Set("other", "other-value");
        client.Set("watched", "v2");

        updates.Should().HaveCount(2);
        updates[0].GetValue<string>().Should().Be("v1");
        updates[1].GetValue<string>().Should().Be("v2");
    }

    [Fact]
    public void Delete_RemovesConfig()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["to-delete"] = "value"
        });

        client.Get<string>("to-delete").Should().Be("value");

        var deleted = client.Delete("to-delete");

        deleted.Should().BeTrue();

        var act = () => client.Get<string>("to-delete");
        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        using var client = TestClient.Create();

        var deleted = client.Delete("non-existent");

        deleted.Should().BeFalse();
    }

    [Fact]
    public void IsInitialized_AlwaysTrue()
    {
        using var client = TestClient.Create();

        client.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void Close_PreventsOperations()
    {
        var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["config"] = "value"
        });

        client.Get<string>("config").Should().Be("value");

        client.Close();

        var act = () => client.Get<string>("config");
        act.Should().Throw<ClientClosedException>();
    }

    [Fact]
    public void Configs_Property_ReturnsAllConfigs()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["a"] = 1,
            ["b"] = 2
        });

        var configs = client.Configs;

        configs.Should().HaveCount(2);
        configs.Should().ContainKey("a");
        configs.Should().ContainKey("b");
    }

    [Fact]
    public void AndCondition_AllMustMatch()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "config",
            value: "default",
            overrides:
            [
                new OverrideData
                {
                    Name = "both-conditions",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "and",
                            Conditions =
                            [
                                new ConditionData { Operator = "equals", Property = "a", Expected = 1 },
                                new ConditionData { Operator = "equals", Property = "b", Expected = 2 }
                            ]
                        }
                    ],
                    Value = "both-matched"
                }
            ]);

        client.Get<string>("config", new ReplaneContext { ["a"] = 1, ["b"] = 2 })
            .Should().Be("both-matched");

        client.Get<string>("config", new ReplaneContext { ["a"] = 1, ["b"] = 999 })
            .Should().Be("default");
    }

    [Fact]
    public void OrCondition_AnyCanMatch()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "config",
            value: "default",
            overrides:
            [
                new OverrideData
                {
                    Name = "either-condition",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "or",
                            Conditions =
                            [
                                new ConditionData { Operator = "equals", Property = "plan", Expected = "pro" },
                                new ConditionData { Operator = "equals", Property = "plan", Expected = "enterprise" }
                            ]
                        }
                    ],
                    Value = "premium-value"
                }
            ]);

        client.Get<string>("config", new ReplaneContext { ["plan"] = "pro" })
            .Should().Be("premium-value");

        client.Get<string>("config", new ReplaneContext { ["plan"] = "enterprise" })
            .Should().Be("premium-value");

        client.Get<string>("config", new ReplaneContext { ["plan"] = "free" })
            .Should().Be("default");
    }

    [Fact]
    public void NotCondition_InvertsResult()
    {
        using var client = TestClient.Create();

        client.SetConfigWithOverrides(
            "config",
            value: "default",
            overrides:
            [
                new OverrideData
                {
                    Name = "not-blocked",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "not",
                            Condition = new ConditionData
                            {
                                Operator = "equals",
                                Property = "status",
                                Expected = "blocked"
                            }
                        }
                    ],
                    Value = "allowed"
                }
            ]);

        client.Get<string>("config", new ReplaneContext { ["status"] = "active" })
            .Should().Be("allowed");

        client.Get<string>("config", new ReplaneContext { ["status"] = "blocked" })
            .Should().Be("default");
    }

    // Complex type tests

    [Fact]
    public void Get_ComplexType_DeserializesCorrectly()
    {
        var themeConfig = new ThemeConfig
        {
            DarkMode = true,
            PrimaryColor = "#3B82F6",
            FontSize = 14
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme"] = themeConfig
        });

        var result = client.Get<ThemeConfig>("theme");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#3B82F6");
        result.FontSize.Should().Be(14);
    }

    [Fact]
    public void Get_ComplexTypeWithNestedLists_DeserializesCorrectly()
    {
        var featureFlags = new FeatureFlags
        {
            NewUI = true,
            BetaFeatures = false,
            EnabledModules = ["dashboard", "analytics", "reports"]
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["features"] = featureFlags
        });

        var result = client.Get<FeatureFlags>("features");

        result.Should().NotBeNull();
        result!.NewUI.Should().BeTrue();
        result.BetaFeatures.Should().BeFalse();
        result.EnabledModules.Should().BeEquivalentTo(["dashboard", "analytics", "reports"]);
    }

    [Fact]
    public void Get_ComplexTypeWithDictionary_DeserializesCorrectly()
    {
        var apiConfig = new ApiConfig
        {
            Endpoint = "https://api.example.com",
            TimeoutMs = 5000,
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token",
                ["X-Custom-Header"] = "value"
            }
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["api-config"] = apiConfig
        });

        var result = client.Get<ApiConfig>("api-config");

        result.Should().NotBeNull();
        result!.Endpoint.Should().Be("https://api.example.com");
        result.TimeoutMs.Should().Be(5000);
        result.Headers.Should().ContainKey("Authorization");
        result.Headers["Authorization"].Should().Be("Bearer token");
    }

    [Fact]
    public void Get_ComplexTypeWithOverrides_EvaluatesAndDeserializesCorrectly()
    {
        using var client = TestClient.Create();

        var defaultTheme = new ThemeConfig { DarkMode = false, PrimaryColor = "#000000", FontSize = 12 };
        var premiumTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#FFD700", FontSize = 16 };

        client.SetConfigWithOverrides(
            "theme",
            value: defaultTheme,
            overrides:
            [
                new OverrideData
                {
                    Name = "premium-theme",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "equals",
                            Property = "plan",
                            Expected = "premium"
                        }
                    ],
                    Value = premiumTheme
                }
            ]);

        // Free user gets default theme
        var freeTheme = client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "free" });
        freeTheme!.DarkMode.Should().BeFalse();
        freeTheme.PrimaryColor.Should().Be("#000000");

        // Premium user gets premium theme
        var pTheme = client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "premium" });
        pTheme!.DarkMode.Should().BeTrue();
        pTheme.PrimaryColor.Should().Be("#FFD700");
    }

    [Fact]
    public void ConfigChanged_GetValue_DeserializesComplexType()
    {
        using var client = TestClient.Create();

        var receivedThemes = new List<ThemeConfig?>();
        client.ConfigChanged += (sender, e) =>
        {
            if (e.ConfigName == "theme")
            {
                receivedThemes.Add(e.GetValue<ThemeConfig>());
            }
        };

        client.Set("theme", new ThemeConfig { DarkMode = true, PrimaryColor = "#FF0000", FontSize = 14 });
        client.Set("theme", new ThemeConfig { DarkMode = false, PrimaryColor = "#00FF00", FontSize = 16 });

        receivedThemes.Should().HaveCount(2);
        receivedThemes[0]!.DarkMode.Should().BeTrue();
        receivedThemes[0]!.PrimaryColor.Should().Be("#FF0000");
        receivedThemes[1]!.DarkMode.Should().BeFalse();
        receivedThemes[1]!.PrimaryColor.Should().Be("#00FF00");
    }

    [Fact]
    public void Get_AnonymousObjectAsComplexType_DeserializesCorrectly()
    {
        // Test that anonymous objects (like from JSON) deserialize correctly
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["settings"] = new
            {
                darkMode = true,
                primaryColor = "#123456",
                fontSize = 18
            }
        });

        var result = client.Get<ThemeConfig>("settings");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#123456");
        result.FontSize.Should().Be(18);
    }
}
