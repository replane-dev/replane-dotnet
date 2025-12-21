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

// Additional complex types for edge case testing
public record NullableConfig
{
    public string? Name { get; init; }
    public int? Count { get; init; }
    public bool? Enabled { get; init; }
    public ThemeConfig? Theme { get; init; }
}

public record NestedConfig
{
    public string Id { get; init; } = "";
    public ThemeConfig Theme { get; init; } = new();
    public ApiConfig Api { get; init; } = new();
}

public record DeeplyNestedConfig
{
    public string Name { get; init; } = "";
    public NestedConfig Settings { get; init; } = new();
    public List<NestedConfig> Children { get; init; } = [];
}

public record ConfigWithArray
{
    public string Name { get; init; } = "";
    public List<ThemeConfig> Themes { get; init; } = [];
    public string[] Tags { get; init; } = [];
}

public record NumericConfig
{
    public int IntValue { get; init; }
    public long LongValue { get; init; }
    public double DoubleValue { get; init; }
    public float FloatValue { get; init; }
    public decimal DecimalValue { get; init; }
}

public enum UserRole { Guest, User, Admin, SuperAdmin }

public record ConfigWithEnum
{
    public UserRole Role { get; init; }
    public List<UserRole> AllowedRoles { get; init; } = [];
}

public record ConfigWithDateTime
{
    public DateTime CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
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

    // Additional complex type edge case tests

    [Fact]
    public void Get_ComplexTypeWithNullableProperties_HandlesNulls()
    {
        var config = new NullableConfig
        {
            Name = null,
            Count = null,
            Enabled = null,
            Theme = null
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["nullable-config"] = config
        });

        var result = client.Get<NullableConfig>("nullable-config");

        result.Should().NotBeNull();
        result!.Name.Should().BeNull();
        result.Count.Should().BeNull();
        result.Enabled.Should().BeNull();
        result.Theme.Should().BeNull();
    }

    [Fact]
    public void Get_ComplexTypeWithNullableProperties_HandlesValues()
    {
        var config = new NullableConfig
        {
            Name = "test",
            Count = 42,
            Enabled = true,
            Theme = new ThemeConfig { DarkMode = true, PrimaryColor = "#FFF", FontSize = 12 }
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["nullable-config"] = config
        });

        var result = client.Get<NullableConfig>("nullable-config");

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Count.Should().Be(42);
        result.Enabled.Should().BeTrue();
        result.Theme.Should().NotBeNull();
        result.Theme!.DarkMode.Should().BeTrue();
    }

    [Fact]
    public void Get_NestedComplexTypes_DeserializesCorrectly()
    {
        var config = new NestedConfig
        {
            Id = "config-123",
            Theme = new ThemeConfig { DarkMode = true, PrimaryColor = "#FF0000", FontSize = 14 },
            Api = new ApiConfig
            {
                Endpoint = "https://api.test.com",
                TimeoutMs = 3000,
                Headers = new Dictionary<string, string> { ["X-Api-Key"] = "secret" }
            }
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["nested"] = config
        });

        var result = client.Get<NestedConfig>("nested");

        result.Should().NotBeNull();
        result!.Id.Should().Be("config-123");
        result.Theme.DarkMode.Should().BeTrue();
        result.Theme.PrimaryColor.Should().Be("#FF0000");
        result.Api.Endpoint.Should().Be("https://api.test.com");
        result.Api.Headers["X-Api-Key"].Should().Be("secret");
    }

    [Fact]
    public void Get_DeeplyNestedComplexTypes_DeserializesCorrectly()
    {
        var config = new DeeplyNestedConfig
        {
            Name = "root",
            Settings = new NestedConfig
            {
                Id = "settings-1",
                Theme = new ThemeConfig { DarkMode = true, PrimaryColor = "#000", FontSize = 12 },
                Api = new ApiConfig { Endpoint = "https://main.api.com", TimeoutMs = 5000 }
            },
            Children =
            [
                new NestedConfig
                {
                    Id = "child-1",
                    Theme = new ThemeConfig { DarkMode = false, PrimaryColor = "#FFF", FontSize = 14 },
                    Api = new ApiConfig { Endpoint = "https://child1.api.com", TimeoutMs = 3000 }
                },
                new NestedConfig
                {
                    Id = "child-2",
                    Theme = new ThemeConfig { DarkMode = true, PrimaryColor = "#AAA", FontSize = 16 },
                    Api = new ApiConfig { Endpoint = "https://child2.api.com", TimeoutMs = 2000 }
                }
            ]
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["deeply-nested"] = config
        });

        var result = client.Get<DeeplyNestedConfig>("deeply-nested");

        result.Should().NotBeNull();
        result!.Name.Should().Be("root");
        result.Settings.Id.Should().Be("settings-1");
        result.Settings.Theme.DarkMode.Should().BeTrue();
        result.Children.Should().HaveCount(2);
        result.Children[0].Id.Should().Be("child-1");
        result.Children[1].Id.Should().Be("child-2");
        result.Children[0].Theme.DarkMode.Should().BeFalse();
        result.Children[1].Theme.DarkMode.Should().BeTrue();
    }

    [Fact]
    public void Get_ComplexTypeWithArrayOfObjects_DeserializesCorrectly()
    {
        var config = new ConfigWithArray
        {
            Name = "multi-theme",
            Themes =
            [
                new ThemeConfig { DarkMode = true, PrimaryColor = "#000", FontSize = 12 },
                new ThemeConfig { DarkMode = false, PrimaryColor = "#FFF", FontSize = 14 },
                new ThemeConfig { DarkMode = true, PrimaryColor = "#333", FontSize = 16 }
            ],
            Tags = ["featured", "new", "popular"]
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["array-config"] = config
        });

        var result = client.Get<ConfigWithArray>("array-config");

        result.Should().NotBeNull();
        result!.Name.Should().Be("multi-theme");
        result.Themes.Should().HaveCount(3);
        result.Themes[0].DarkMode.Should().BeTrue();
        result.Themes[1].DarkMode.Should().BeFalse();
        result.Themes[2].PrimaryColor.Should().Be("#333");
        result.Tags.Should().BeEquivalentTo(["featured", "new", "popular"]);
    }

    [Fact]
    public void Get_ComplexTypeWithEmptyCollections_DeserializesCorrectly()
    {
        var config = new ConfigWithArray
        {
            Name = "empty",
            Themes = [],
            Tags = []
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["empty-arrays"] = config
        });

        var result = client.Get<ConfigWithArray>("empty-arrays");

        result.Should().NotBeNull();
        result!.Name.Should().Be("empty");
        result.Themes.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Get_ComplexTypeNotFound_WithDefault_ReturnsDefault()
    {
        using var client = TestClient.Create();

        var defaultTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#DEFAULT", FontSize = 10 };
        var result = client.Get("missing-theme", defaultValue: defaultTheme);

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#DEFAULT");
    }

    [Fact]
    public void Get_ComplexTypeNotFound_ThrowsException()
    {
        using var client = TestClient.Create();

        var act = () => client.Get<ThemeConfig>("missing-theme");

        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public void Get_NullConfigValue_ReturnsNull()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["null-config"] = null
        });

        var result = client.Get<ThemeConfig?>("null-config");

        result.Should().BeNull();
    }

    [Fact]
    public void Get_NumericTypes_DeserializesCorrectly()
    {
        var config = new NumericConfig
        {
            IntValue = 42,
            LongValue = 9999999999L,
            DoubleValue = 3.14159,
            FloatValue = 2.5f,
            DecimalValue = 123.45m
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["numeric"] = config
        });

        var result = client.Get<NumericConfig>("numeric");

        result.Should().NotBeNull();
        result!.IntValue.Should().Be(42);
        result.LongValue.Should().Be(9999999999L);
        result.DoubleValue.Should().BeApproximately(3.14159, 0.00001);
        result.FloatValue.Should().BeApproximately(2.5f, 0.001f);
        result.DecimalValue.Should().Be(123.45m);
    }

    [Fact]
    public void Get_EnumType_DeserializesCorrectly()
    {
        var config = new ConfigWithEnum
        {
            Role = UserRole.Admin,
            AllowedRoles = [UserRole.User, UserRole.Admin, UserRole.SuperAdmin]
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["enum-config"] = config
        });

        var result = client.Get<ConfigWithEnum>("enum-config");

        result.Should().NotBeNull();
        result!.Role.Should().Be(UserRole.Admin);
        result.AllowedRoles.Should().BeEquivalentTo([UserRole.User, UserRole.Admin, UserRole.SuperAdmin]);
    }

    [Fact]
    public void Get_DateTimeTypes_DeserializesCorrectly()
    {
        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        var config = new ConfigWithDateTime
        {
            CreatedAt = now,
            UpdatedAt = nowOffset,
            DeletedAt = null
        };

        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["datetime-config"] = config
        });

        var result = client.Get<ConfigWithDateTime>("datetime-config");

        result.Should().NotBeNull();
        result!.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        result.UpdatedAt.Should().BeCloseTo(nowOffset, TimeSpan.FromSeconds(1));
        result.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Get_ComplexTypeWithMultipleOverrides_SelectsCorrectOverride()
    {
        using var client = TestClient.Create();

        var defaultTheme = new ThemeConfig { DarkMode = false, PrimaryColor = "#000", FontSize = 12 };
        var premiumTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#GOLD", FontSize = 14 };
        var enterpriseTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#PLATINUM", FontSize = 16 };

        client.SetConfigWithOverrides(
            "theme",
            value: defaultTheme,
            overrides:
            [
                new OverrideData
                {
                    Name = "enterprise",
                    Conditions = [new ConditionData { Operator = "equals", Property = "plan", Expected = "enterprise" }],
                    Value = enterpriseTheme
                },
                new OverrideData
                {
                    Name = "premium",
                    Conditions = [new ConditionData { Operator = "equals", Property = "plan", Expected = "premium" }],
                    Value = premiumTheme
                }
            ]);

        client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "free" })!
            .PrimaryColor.Should().Be("#000");

        client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "premium" })!
            .PrimaryColor.Should().Be("#GOLD");

        client.Get<ThemeConfig>("theme", new ReplaneContext { ["plan"] = "enterprise" })!
            .PrimaryColor.Should().Be("#PLATINUM");
    }

    [Fact]
    public void Get_ComplexTypeWithSegmentation_SplitsCorrectly()
    {
        using var client = TestClient.Create();

        var controlTheme = new ThemeConfig { DarkMode = false, PrimaryColor = "#CONTROL", FontSize = 12 };
        var treatmentTheme = new ThemeConfig { DarkMode = true, PrimaryColor = "#TREATMENT", FontSize = 14 };

        client.SetConfigWithOverrides(
            "ab-theme",
            value: controlTheme,
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
                            Seed = "theme-experiment"
                        }
                    ],
                    Value = treatmentTheme
                }
            ]);

        // Collect results for many users
        var controlCount = 0;
        var treatmentCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var theme = client.Get<ThemeConfig>("ab-theme", new ReplaneContext { ["user_id"] = $"user-{i}" });
            if (theme!.PrimaryColor == "#CONTROL") controlCount++;
            else if (theme.PrimaryColor == "#TREATMENT") treatmentCount++;
        }

        // Both variants should have significant representation
        controlCount.Should().BeGreaterThan(20);
        treatmentCount.Should().BeGreaterThan(20);
    }

    [Fact]
    public void Get_ComplexTypeWithAndCondition_RequiresAllConditions()
    {
        using var client = TestClient.Create();

        var defaultConfig = new ApiConfig { Endpoint = "https://default.api.com", TimeoutMs = 1000 };
        var specialConfig = new ApiConfig { Endpoint = "https://special.api.com", TimeoutMs = 5000 };

        client.SetConfigWithOverrides(
            "api",
            value: defaultConfig,
            overrides:
            [
                new OverrideData
                {
                    Name = "special-case",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "and",
                            Conditions =
                            [
                                new ConditionData { Operator = "equals", Property = "region", Expected = "us-east" },
                                new ConditionData { Operator = "equals", Property = "tier", Expected = "premium" }
                            ]
                        }
                    ],
                    Value = specialConfig
                }
            ]);

        // Only region matches
        client.Get<ApiConfig>("api", new ReplaneContext { ["region"] = "us-east", ["tier"] = "free" })!
            .Endpoint.Should().Be("https://default.api.com");

        // Only tier matches
        client.Get<ApiConfig>("api", new ReplaneContext { ["region"] = "eu-west", ["tier"] = "premium" })!
            .Endpoint.Should().Be("https://default.api.com");

        // Both match
        client.Get<ApiConfig>("api", new ReplaneContext { ["region"] = "us-east", ["tier"] = "premium" })!
            .Endpoint.Should().Be("https://special.api.com");
    }

    [Fact]
    public void Set_ComplexType_UpdatesExisting()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme"] = new ThemeConfig { DarkMode = false, PrimaryColor = "#OLD", FontSize = 10 }
        });

        client.Get<ThemeConfig>("theme")!.PrimaryColor.Should().Be("#OLD");

        client.Set("theme", new ThemeConfig { DarkMode = true, PrimaryColor = "#NEW", FontSize = 20 });

        var updated = client.Get<ThemeConfig>("theme");
        updated!.DarkMode.Should().BeTrue();
        updated.PrimaryColor.Should().Be("#NEW");
        updated.FontSize.Should().Be(20);
    }

    [Fact]
    public void ConfigChanged_ComplexType_TracksMultipleUpdates()
    {
        using var client = TestClient.Create();

        var updates = new List<ThemeConfig?>();
        client.ConfigChanged += (sender, e) =>
        {
            if (e.ConfigName == "theme")
            {
                updates.Add(e.GetValue<ThemeConfig>());
            }
        };

        client.Set("theme", new ThemeConfig { DarkMode = false, PrimaryColor = "#V1", FontSize = 10 });
        client.Set("theme", new ThemeConfig { DarkMode = true, PrimaryColor = "#V2", FontSize = 12 });
        client.Set("theme", new ThemeConfig { DarkMode = false, PrimaryColor = "#V3", FontSize = 14 });

        updates.Should().HaveCount(3);
        updates[0]!.PrimaryColor.Should().Be("#V1");
        updates[1]!.PrimaryColor.Should().Be("#V2");
        updates[2]!.PrimaryColor.Should().Be("#V3");
    }

    [Fact]
    public void ConfigChanged_NestedComplexType_DeserializesCorrectly()
    {
        using var client = TestClient.Create();

        NestedConfig? received = null;
        client.ConfigChanged += (sender, e) =>
        {
            if (e.ConfigName == "nested")
            {
                received = e.GetValue<NestedConfig>();
            }
        };

        client.Set("nested", new NestedConfig
        {
            Id = "test-123",
            Theme = new ThemeConfig { DarkMode = true, PrimaryColor = "#AAA", FontSize = 12 },
            Api = new ApiConfig { Endpoint = "https://test.com", TimeoutMs = 3000 }
        });

        received.Should().NotBeNull();
        received!.Id.Should().Be("test-123");
        received.Theme.DarkMode.Should().BeTrue();
        received.Api.Endpoint.Should().Be("https://test.com");
    }

    [Fact]
    public void Get_DictionaryWithComplexValues_DeserializesCorrectly()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme-map"] = new Dictionary<string, ThemeConfig>
            {
                ["light"] = new ThemeConfig { DarkMode = false, PrimaryColor = "#FFF", FontSize = 12 },
                ["dark"] = new ThemeConfig { DarkMode = true, PrimaryColor = "#000", FontSize = 12 }
            }
        });

        var result = client.Get<Dictionary<string, ThemeConfig>>("theme-map");

        result.Should().NotBeNull();
        result.Should().ContainKey("light");
        result.Should().ContainKey("dark");
        result!["light"].DarkMode.Should().BeFalse();
        result["dark"].DarkMode.Should().BeTrue();
    }

    [Fact]
    public void Get_ListOfComplexTypes_DeserializesCorrectly()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme-list"] = new List<ThemeConfig>
            {
                new() { DarkMode = false, PrimaryColor = "#111", FontSize = 10 },
                new() { DarkMode = true, PrimaryColor = "#222", FontSize = 12 },
                new() { DarkMode = false, PrimaryColor = "#333", FontSize = 14 }
            }
        });

        var result = client.Get<List<ThemeConfig>>("theme-list");

        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result![0].PrimaryColor.Should().Be("#111");
        result[1].PrimaryColor.Should().Be("#222");
        result[2].PrimaryColor.Should().Be("#333");
    }

    [Fact]
    public void Get_ComplexType_CamelCaseJson_DeserializesCorrectly()
    {
        // Simulating JSON with camelCase property names (common in APIs)
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["settings"] = new
            {
                darkMode = true,
                primaryColor = "#CAMEL",
                fontSize = 15
            }
        });

        var result = client.Get<ThemeConfig>("settings");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#CAMEL");
        result.FontSize.Should().Be(15);
    }

    [Fact]
    public void Get_ComplexType_MissingOptionalProperties_UsesDefaults()
    {
        // JSON with missing properties - should use record defaults
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["partial"] = new { darkMode = true }  // Missing primaryColor and fontSize
        });

        var result = client.Get<ThemeConfig>("partial");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("");  // Default from record
        result.FontSize.Should().Be(0);       // Default for int
    }

    [Fact]
    public void Delete_ComplexType_RemovesConfig()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme"] = new ThemeConfig { DarkMode = true, PrimaryColor = "#DEL", FontSize = 12 }
        });

        client.Get<ThemeConfig>("theme").Should().NotBeNull();

        var deleted = client.Delete("theme");

        deleted.Should().BeTrue();
        var act = () => client.Get<ThemeConfig>("theme");
        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public void Configs_Property_ContainsComplexTypes()
    {
        using var client = TestClient.Create(new Dictionary<string, object?>
        {
            ["theme"] = new ThemeConfig { DarkMode = true, PrimaryColor = "#TEST", FontSize = 12 },
            ["api"] = new ApiConfig { Endpoint = "https://test.com", TimeoutMs = 5000 }
        });

        var configs = client.Configs;

        configs.Should().HaveCount(2);
        configs.Should().ContainKey("theme");
        configs.Should().ContainKey("api");

        // Can deserialize from stored configs
        configs["theme"].GetValue<ThemeConfig>()!.PrimaryColor.Should().Be("#TEST");
        configs["api"].GetValue<ApiConfig>()!.Endpoint.Should().Be("https://test.com");
    }
}
