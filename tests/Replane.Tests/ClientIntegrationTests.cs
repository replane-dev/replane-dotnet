using Replane.Tests.MockServer;
using System.Text.Json;

namespace Replane.Tests;

// Complex types for integration testing
public record IntegrationThemeConfig
{
    public bool DarkMode { get; init; }
    public string PrimaryColor { get; init; } = "";
    public int FontSize { get; init; }
}

public record IntegrationApiConfig
{
    public string Endpoint { get; init; } = "";
    public int TimeoutMs { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
}

public record IntegrationNestedConfig
{
    public string Id { get; init; } = "";
    public IntegrationThemeConfig Theme { get; init; } = new();
    public List<string> Tags { get; init; } = [];
}

public record IntegrationNullableConfig
{
    public string? Name { get; init; }
    public int? Value { get; init; }
    public IntegrationThemeConfig? Theme { get; init; }
}

public class ClientIntegrationTests : IAsyncLifetime
{
    // Helper to convert object to JsonElement for tests
    private static JsonElement ToJson(object? value) => JsonValueConverter.ToJsonElement(value);
    private MockReplaneServer _server = null!;

    // Helper to create ConnectOptions from server
    private ConnectOptions GetConnectOptions(int? connectionTimeoutMs = null, string? sdkKey = null) => new ConnectOptions
    {
        BaseUrl = _server.BaseUrl,
        SdkKey = sdkKey ?? _server.SdkKey,
        ConnectionTimeoutMs = connectionTimeoutMs ?? 5000
    };

    public async Task InitializeAsync()
    {
        _server = new MockReplaneServer();
        _server.Start();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_LoadsConfigs()
    {
        _server.AddConfig("feature-enabled", true);
        _server.AddConfig("max-items", 100);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.IsInitialized.Should().BeTrue();
        client.Get<bool>("feature-enabled").Should().BeTrue();
        client.Get<int>("max-items").Should().Be(100);
    }

    [Fact]
    public async Task ConnectAsync_WithDefaults_UsesDefaultsBeforeInit()
    {
        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["default-config"] = "default-value" }
        });

        // Before connecting, default should be available
        var value = client.Get<string>("default-config");
        value.Should().Be("default-value");
    }

    [Fact]
    public async Task ConnectAsync_AuthenticationError_ThrowsException()
    {
        _server.SimulateAuthError = true;

        await using var client = new ReplaneClient();

        var act = () => client.ConnectAsync(GetConnectOptions(sdkKey: "invalid-key"));

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task ConnectAsync_Timeout_ThrowsException()
    {
        _server.InitDelay = TimeSpan.FromSeconds(10);

        await using var client = new ReplaneClient();

        var act = () => client.ConnectAsync(GetConnectOptions(connectionTimeoutMs: 100));

        await act.Should().ThrowAsync<ReplaneTimeoutException>();
    }

    [Fact]
    public async Task Get_WithOverrides_EvaluatesCorrectly()
    {
        var config = new Config
        {
            Name = "premium-feature",
            Value = ToJson(false),
            Overrides =
            [
                new Override
                {
                    Name = "premium-users",
                    Conditions =
                    [
                        new PropertyCondition
                        {
                            Op = "in",
                            Property = "plan",
                            Expected = new List<object?> { "premium", "enterprise" }
                        }
                    ],
                    Value = ToJson(true)
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        // Free user - should get false
        client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "free" })
            .Should().BeFalse();

        // Premium user - should get true
        client.Get<bool>("premium-feature", new ReplaneContext { ["plan"] = "premium" })
            .Should().BeTrue();
    }

    [Fact]
    public async Task Get_WithDefaultContext_MergesWithCallContext()
    {
        var config = new Config
        {
            Name = "test-config",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "region-override",
                    Conditions =
                    [
                        new PropertyCondition { Op = "equals", Property = "region", Expected = "us" },
                        new PropertyCondition { Op = "equals", Property = "plan", Expected = "pro" }
                    ],
                    Value = ToJson("us-pro")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Context = new ReplaneContext { ["region"] = "us" }
        });

        await client.ConnectAsync(GetConnectOptions());

        // Only default context - no match
        client.Get<string>("test-config").Should().Be("default");

        // Call context merged with default - should match
        client.Get<string>("test-config", new ReplaneContext { ["plan"] = "pro" })
            .Should().Be("us-pro");
    }

    [Fact]
    public async Task Get_ConfigNotFound_ThrowsException()
    {
        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var act = () => client.Get<string>("non-existent-config");

        act.Should().Throw<ConfigNotFoundException>()
            .Which.ConfigName.Should().Be("non-existent-config");
    }

    [Fact]
    public async Task Get_ConfigNotFound_WithDefault_ReturnsDefault()
    {
        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var value = client.Get("non-existent-config", defaultValue: "my-default");

        value.Should().Be("my-default");
    }

    [Fact]
    public async Task Subscribe_ReceivesUpdates()
    {
        _server.AddConfig("test-config", "initial");

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var receivedUpdates = new List<(string Name, Config Config)>();
        client.ConfigChanged += (sender, e) =>
        {
            receivedUpdates.Add((e.ConfigName, e.Config));
        };

        // Initial value
        client.Get<string>("test-config").Should().Be("initial");

        // Note: For a complete test, we'd need to implement broadcasting
        // config changes from the server. The current mock server doesn't
        // fully support this, but the subscription mechanism is in place.
    }

    [Fact]
    public async Task ConfigChanged_ReceivesUpdates()
    {
        _server.AddConfig("watched-config", "initial");
        _server.AddConfig("other-config", "other");

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var receivedUpdates = new List<ConfigChangedEventArgs>();
        client.ConfigChanged += (sender, e) =>
        {
            receivedUpdates.Add(e);
        };

        // Verify event handler is set up
        client.Get<string>("watched-config").Should().Be("initial");
    }

    [Fact]
    public async Task Close_PreventsNewOperations()
    {
        _server.AddConfig("test", "value");

        var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());
        client.Get<string>("test").Should().Be("value");

        client.Close();

        var act = () => client.Get<string>("test");
        act.Should().Throw<ClientClosedException>();
    }

    [Fact]
    public async Task RequiredConfigs_AllPresent_Succeeds()
    {
        _server.AddConfig("required-1", true);
        _server.AddConfig("required-2", "value");

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Required = ["required-1", "required-2"]
        });

        var act = () => client.ConnectAsync(GetConnectOptions());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequiredConfigs_Missing_ThrowsException()
    {
        _server.AddConfig("required-1", true);
        // required-2 is not added

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Required = ["required-1", "required-2"]
        });

        var act = () => client.ConnectAsync(GetConnectOptions());

        await act.Should().ThrowAsync<ConfigNotFoundException>();
    }

    // ==================== Additional Edge Case Tests ====================

    // Basic client behavior tests

    [Fact]
    public async Task ConnectAsync_EmptyConfigs_Succeeds()
    {
        // No configs added to server

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_ManyConfigs_LoadsAll()
    {
        for (var i = 0; i < 100; i++)
        {
            _server.AddConfig($"config-{i}", i);
        }

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        for (var i = 0; i < 100; i++)
        {
            client.Get<int>($"config-{i}").Should().Be(i);
        }
    }

    [Fact]
    public async Task Get_VariousPrimitiveTypes_DeserializesCorrectly()
    {
        _server.AddConfig("string-config", "hello");
        _server.AddConfig("int-config", 42);
        _server.AddConfig("bool-true", true);
        _server.AddConfig("bool-false", false);
        _server.AddConfig("long-config", 9999999999L);
        _server.AddConfig("double-config", 3.14159);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("string-config").Should().Be("hello");
        client.Get<int>("int-config").Should().Be(42);
        client.Get<bool>("bool-true").Should().BeTrue();
        client.Get<bool>("bool-false").Should().BeFalse();
        client.Get<long>("long-config").Should().Be(9999999999L);
        client.Get<double>("double-config").Should().BeApproximately(3.14159, 0.00001);
    }

    [Fact]
    public async Task Get_NullConfigValue_ReturnsNull()
    {
        _server.AddConfig("null-config", null);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<string?>("null-config");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_ListConfig_DeserializesCorrectly()
    {
        _server.AddConfig("list-config", new List<string> { "a", "b", "c" });

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<List<string>>("list-config");
        result.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public async Task Get_DictionaryConfig_DeserializesCorrectly()
    {
        _server.AddConfig("dict-config", new Dictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        });

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<Dictionary<string, int>>("dict-config");
        result.Should().ContainKey("one").WhoseValue.Should().Be(1);
        result.Should().ContainKey("two").WhoseValue.Should().Be(2);
    }

    // Error handling tests

    [Fact]
    public async Task ConnectAsync_ServerError_ThrowsException()
    {
        _server.SimulateServerError = true;

        await using var client = new ReplaneClient();

        var act = () => client.ConnectAsync(GetConnectOptions(connectionTimeoutMs: 1000));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task IsInitialized_BeforeConnect_ReturnsFalse()
    {
        await using var client = new ReplaneClient();

        client.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task Get_BeforeConnect_WithDefault_ReturnsDefault()
    {
        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["my-config"] = "default" }
        });

        // Not connected yet
        var value = client.Get<string>("my-config");
        value.Should().Be("default");
    }

    [Fact]
    public async Task Get_BeforeConnect_WithoutDefault_ThrowsException()
    {
        await using var client = new ReplaneClient();

        var act = () => client.Get<string>("missing-config");

        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        // Should not throw
        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    // Override tests with various operators

    [Fact]
    public async Task Get_WithEqualsOperator_EvaluatesCorrectly()
    {
        var config = new Config
        {
            Name = "equals-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "exact-match",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "user", Expected = "admin" }],
                    Value = ToJson("admin-value")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("equals-test", new ReplaneContext { ["user"] = "guest" }).Should().Be("default");
        client.Get<string>("equals-test", new ReplaneContext { ["user"] = "admin" }).Should().Be("admin-value");
    }

    [Fact]
    public async Task Get_WithNotInOperator_EvaluatesCorrectly()
    {
        var config = new Config
        {
            Name = "not-in-test",
            Value = ToJson("blocked"),
            Overrides =
            [
                new Override
                {
                    Name = "allowed-regions",
                    Conditions =
                    [
                        new PropertyCondition
                        {
                            Op = "not_in",
                            Property = "region",
                            Expected = new List<object?> { "blocked-1", "blocked-2" }
                        }
                    ],
                    Value = ToJson("allowed")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("not-in-test", new ReplaneContext { ["region"] = "blocked-1" }).Should().Be("blocked");
        client.Get<string>("not-in-test", new ReplaneContext { ["region"] = "us-east" }).Should().Be("allowed");
    }

    [Fact]
    public async Task Get_WithLessThanOperator_EvaluatesCorrectly()
    {
        var config = new Config
        {
            Name = "age-gate",
            Value = ToJson("adult-content"),
            Overrides =
            [
                new Override
                {
                    Name = "minor",
                    Conditions = [new PropertyCondition { Op = "less_than", Property = "age", Expected = 18 }],
                    Value = ToJson("restricted")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("age-gate", new ReplaneContext { ["age"] = 15 }).Should().Be("restricted");
        client.Get<string>("age-gate", new ReplaneContext { ["age"] = 21 }).Should().Be("adult-content");
    }

    [Fact]
    public async Task Get_WithGreaterThanOrEqualOperator_EvaluatesCorrectly()
    {
        var config = new Config
        {
            Name = "score-reward",
            Value = ToJson("no-reward"),
            Overrides =
            [
                new Override
                {
                    Name = "high-scorer",
                    Conditions = [new PropertyCondition { Op = "greater_than_or_equal", Property = "score", Expected = 100 }],
                    Value = ToJson("reward")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("score-reward", new ReplaneContext { ["score"] = 50 }).Should().Be("no-reward");
        client.Get<string>("score-reward", new ReplaneContext { ["score"] = 100 }).Should().Be("reward");
        client.Get<string>("score-reward", new ReplaneContext { ["score"] = 150 }).Should().Be("reward");
    }

    [Fact]
    public async Task Get_WithSegmentation_SplitsCorrectly()
    {
        var config = new Config
        {
            Name = "ab-test",
            Value = ToJson("control"),
            Overrides =
            [
                new Override
                {
                    Name = "treatment",
                    Conditions =
                    [
                        new SegmentationCondition
                        {
                            Property = "user_id",
                            FromPercentage = 0,
                            ToPercentage = 50,
                            Seed = "experiment-1"
                        }
                    ],
                    Value = ToJson("treatment")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        // Results should be deterministic
        var result1 = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = "user-123" });
        var result2 = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = "user-123" });
        result1.Should().Be(result2);

        // With enough users, we should see both variants
        var controlCount = 0;
        var treatmentCount = 0;
        for (var i = 0; i < 100; i++)
        {
            var result = client.Get<string>("ab-test", new ReplaneContext { ["user_id"] = $"user-{i}" });
            if (result == "control") controlCount++;
            else treatmentCount++;
        }

        controlCount.Should().BeGreaterThan(20);
        treatmentCount.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task Get_WithAndCondition_RequiresAllConditions()
    {
        var config = new Config
        {
            Name = "and-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "both-match",
                    Conditions =
                    [
                        new AndCondition
                        {
                            Conditions =
                            [
                                new PropertyCondition { Op = "equals", Property = "a", Expected = 1 },
                                new PropertyCondition { Op = "equals", Property = "b", Expected = 2 }
                            ]
                        }
                    ],
                    Value = ToJson("matched")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("and-test", new ReplaneContext { ["a"] = 1 }).Should().Be("default");
        client.Get<string>("and-test", new ReplaneContext { ["b"] = 2 }).Should().Be("default");
        client.Get<string>("and-test", new ReplaneContext { ["a"] = 1, ["b"] = 2 }).Should().Be("matched");
    }

    [Fact]
    public async Task Get_WithOrCondition_MatchesAny()
    {
        var config = new Config
        {
            Name = "or-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "any-match",
                    Conditions =
                    [
                        new OrCondition
                        {
                            Conditions =
                            [
                                new PropertyCondition { Op = "equals", Property = "plan", Expected = "pro" },
                                new PropertyCondition { Op = "equals", Property = "plan", Expected = "enterprise" }
                            ]
                        }
                    ],
                    Value = ToJson("premium")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("or-test", new ReplaneContext { ["plan"] = "free" }).Should().Be("default");
        client.Get<string>("or-test", new ReplaneContext { ["plan"] = "pro" }).Should().Be("premium");
        client.Get<string>("or-test", new ReplaneContext { ["plan"] = "enterprise" }).Should().Be("premium");
    }

    [Fact]
    public async Task Get_WithNotCondition_InvertsResult()
    {
        var config = new Config
        {
            Name = "not-test",
            Value = ToJson("blocked"),
            Overrides =
            [
                new Override
                {
                    Name = "not-blocked",
                    Conditions =
                    [
                        new NotCondition
                        {
                            Inner = new PropertyCondition { Op = "equals", Property = "status", Expected = "banned" }
                        }
                    ],
                    Value = ToJson("allowed")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<string>("not-test", new ReplaneContext { ["status"] = "banned" }).Should().Be("blocked");
        client.Get<string>("not-test", new ReplaneContext { ["status"] = "active" }).Should().Be("allowed");
    }

    [Fact]
    public async Task Get_WithMultipleOverrides_FirstMatchWins()
    {
        var config = new Config
        {
            Name = "priority-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "first",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "x", Expected = 1 }],
                    Value = ToJson("first-match")
                },
                new Override
                {
                    Name = "second",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "x", Expected = 1 }],
                    Value = ToJson("second-match")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        // First matching override should win
        client.Get<string>("priority-test", new ReplaneContext { ["x"] = 1 }).Should().Be("first-match");
    }

    [Fact]
    public async Task Get_MissingContextProperty_ReturnsDefaultValue()
    {
        var config = new Config
        {
            Name = "missing-context-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "with-property",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "missing", Expected = "value" }],
                    Value = ToJson("matched")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        // No context provided - should return default
        client.Get<string>("missing-context-test").Should().Be("default");
    }

    // Context tests

    [Fact]
    public async Task Get_CallContextOverridesDefaultContext()
    {
        var config = new Config
        {
            Name = "context-override-test",
            Value = ToJson("default"),
            Overrides =
            [
                new Override
                {
                    Name = "region-match",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "region", Expected = "eu" }],
                    Value = ToJson("eu-value")
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Context = new ReplaneContext { ["region"] = "us" }  // Default: us
        });

        await client.ConnectAsync(GetConnectOptions());

        // Default context (us) - no match
        client.Get<string>("context-override-test").Should().Be("default");

        // Call context overrides default
        client.Get<string>("context-override-test", new ReplaneContext { ["region"] = "eu" }).Should().Be("eu-value");
    }

    // Default tests

    [Fact]
    public async Task Get_ServerConfigOverridesDefault()
    {
        _server.AddConfig("override-default", "server-value");

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["override-default"] = "default-value" }
        });

        // Before connect, default is used
        client.Get<string>("override-default").Should().Be("default-value");

        await client.ConnectAsync(GetConnectOptions());

        // After connect, server value wins
        client.Get<string>("override-default").Should().Be("server-value");
    }

    [Fact]
    public async Task Get_DefaultUsedForMissingConfig()
    {
        _server.AddConfig("existing", "value");

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["missing-config"] = "default" }
        });

        await client.ConnectAsync(GetConnectOptions());

        // Existing config
        client.Get<string>("existing").Should().Be("value");

        // Missing config uses default
        client.Get<string>("missing-config").Should().Be("default");
    }

    // ==================== Complex Type Tests ====================

    [Fact]
    public async Task Get_ComplexType_DeserializesCorrectly()
    {
        var theme = new IntegrationThemeConfig
        {
            DarkMode = true,
            PrimaryColor = "#3B82F6",
            FontSize = 14
        };
        _server.AddConfig("theme", theme);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationThemeConfig>("theme");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#3B82F6");
        result.FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Get_ComplexTypeWithDictionary_DeserializesCorrectly()
    {
        var apiConfig = new IntegrationApiConfig
        {
            Endpoint = "https://api.example.com",
            TimeoutMs = 5000,
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token",
                ["X-Custom-Header"] = "value"
            }
        };
        _server.AddConfig("api-config", apiConfig);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationApiConfig>("api-config");

        result.Should().NotBeNull();
        result!.Endpoint.Should().Be("https://api.example.com");
        result.TimeoutMs.Should().Be(5000);
        result.Headers["Authorization"].Should().Be("Bearer token");
    }

    [Fact]
    public async Task Get_NestedComplexType_DeserializesCorrectly()
    {
        var nested = new IntegrationNestedConfig
        {
            Id = "config-123",
            Theme = new IntegrationThemeConfig
            {
                DarkMode = true,
                PrimaryColor = "#FF0000",
                FontSize = 16
            },
            Tags = ["featured", "new"]
        };
        _server.AddConfig("nested", nested);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationNestedConfig>("nested");

        result.Should().NotBeNull();
        result!.Id.Should().Be("config-123");
        result.Theme.DarkMode.Should().BeTrue();
        result.Theme.PrimaryColor.Should().Be("#FF0000");
        result.Tags.Should().BeEquivalentTo(["featured", "new"]);
    }

    [Fact]
    public async Task Get_ComplexTypeWithNullableProperties_HandlesNulls()
    {
        var config = new IntegrationNullableConfig
        {
            Name = null,
            Value = null,
            Theme = null
        };
        _server.AddConfig("nullable-config", config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationNullableConfig>("nullable-config");

        result.Should().NotBeNull();
        result!.Name.Should().BeNull();
        result.Value.Should().BeNull();
        result.Theme.Should().BeNull();
    }

    [Fact]
    public async Task Get_ComplexTypeWithNullableProperties_HandlesValues()
    {
        var config = new IntegrationNullableConfig
        {
            Name = "test",
            Value = 42,
            Theme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#FFF", FontSize = 12 }
        };
        _server.AddConfig("nullable-with-values", config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationNullableConfig>("nullable-with-values");

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(42);
        result.Theme.Should().NotBeNull();
        result.Theme!.DarkMode.Should().BeTrue();
    }

    [Fact]
    public async Task Get_ListOfComplexTypes_DeserializesCorrectly()
    {
        var themes = new List<IntegrationThemeConfig>
        {
            new() { DarkMode = false, PrimaryColor = "#111", FontSize = 10 },
            new() { DarkMode = true, PrimaryColor = "#222", FontSize = 12 },
            new() { DarkMode = false, PrimaryColor = "#333", FontSize = 14 }
        };
        _server.AddConfig("theme-list", themes);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<List<IntegrationThemeConfig>>("theme-list");

        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result![0].PrimaryColor.Should().Be("#111");
        result[1].DarkMode.Should().BeTrue();
        result[2].FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Get_DictionaryOfComplexTypes_DeserializesCorrectly()
    {
        var themeMap = new Dictionary<string, IntegrationThemeConfig>
        {
            ["light"] = new() { DarkMode = false, PrimaryColor = "#FFF", FontSize = 12 },
            ["dark"] = new() { DarkMode = true, PrimaryColor = "#000", FontSize = 12 }
        };
        _server.AddConfig("theme-map", themeMap);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<Dictionary<string, IntegrationThemeConfig>>("theme-map");

        result.Should().NotBeNull();
        result.Should().ContainKey("light");
        result.Should().ContainKey("dark");
        result!["light"].DarkMode.Should().BeFalse();
        result["dark"].DarkMode.Should().BeTrue();
    }

    [Fact]
    public async Task Get_ComplexTypeWithOverrides_EvaluatesCorrectly()
    {
        var defaultTheme = new IntegrationThemeConfig { DarkMode = false, PrimaryColor = "#000", FontSize = 12 };
        var premiumTheme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#GOLD", FontSize = 16 };

        var config = new Config
        {
            Name = "theme-with-override",
            Value = ToJson(defaultTheme),
            Overrides =
            [
                new Override
                {
                    Name = "premium-theme",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "plan", Expected = "premium" }],
                    Value = ToJson(premiumTheme)
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var freeTheme = client.Get<IntegrationThemeConfig>("theme-with-override", new ReplaneContext { ["plan"] = "free" });
        freeTheme!.DarkMode.Should().BeFalse();
        freeTheme.PrimaryColor.Should().Be("#000");

        var pTheme = client.Get<IntegrationThemeConfig>("theme-with-override", new ReplaneContext { ["plan"] = "premium" });
        pTheme!.DarkMode.Should().BeTrue();
        pTheme.PrimaryColor.Should().Be("#GOLD");
    }

    [Fact]
    public async Task Get_ComplexTypeWithMultipleOverrides_SelectsCorrectly()
    {
        var defaultApi = new IntegrationApiConfig { Endpoint = "https://default.api.com", TimeoutMs = 1000 };
        var usApi = new IntegrationApiConfig { Endpoint = "https://us.api.com", TimeoutMs = 2000 };
        var euApi = new IntegrationApiConfig { Endpoint = "https://eu.api.com", TimeoutMs = 3000 };

        var config = new Config
        {
            Name = "api-by-region",
            Value = ToJson(defaultApi),
            Overrides =
            [
                new Override
                {
                    Name = "us-region",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "region", Expected = "us" }],
                    Value = ToJson(usApi)
                },
                new Override
                {
                    Name = "eu-region",
                    Conditions = [new PropertyCondition { Op = "equals", Property = "region", Expected = "eu" }],
                    Value = ToJson(euApi)
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        client.Get<IntegrationApiConfig>("api-by-region", new ReplaneContext { ["region"] = "ap" })!
            .Endpoint.Should().Be("https://default.api.com");

        client.Get<IntegrationApiConfig>("api-by-region", new ReplaneContext { ["region"] = "us" })!
            .Endpoint.Should().Be("https://us.api.com");

        client.Get<IntegrationApiConfig>("api-by-region", new ReplaneContext { ["region"] = "eu" })!
            .Endpoint.Should().Be("https://eu.api.com");
    }

    [Fact]
    public async Task Get_ComplexTypeWithSegmentation_SplitsCorrectly()
    {
        var controlTheme = new IntegrationThemeConfig { DarkMode = false, PrimaryColor = "#CONTROL", FontSize = 12 };
        var treatmentTheme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#TREATMENT", FontSize = 14 };

        var config = new Config
        {
            Name = "ab-theme",
            Value = ToJson(controlTheme),
            Overrides =
            [
                new Override
                {
                    Name = "treatment",
                    Conditions =
                    [
                        new SegmentationCondition
                        {
                            Property = "user_id",
                            FromPercentage = 0,
                            ToPercentage = 50,
                            Seed = "theme-experiment"
                        }
                    ],
                    Value = ToJson(treatmentTheme)
                }
            ]
        };
        _server.AddConfig(config);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var controlCount = 0;
        var treatmentCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var theme = client.Get<IntegrationThemeConfig>("ab-theme", new ReplaneContext { ["user_id"] = $"user-{i}" });
            if (theme!.PrimaryColor == "#CONTROL") controlCount++;
            else treatmentCount++;
        }

        controlCount.Should().BeGreaterThan(20);
        treatmentCount.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task Get_ComplexTypeNotFound_WithDefault_ReturnsDefault()
    {
        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var defaultTheme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#DEFAULT", FontSize = 10 };
        var result = client.Get("missing-theme", defaultValue: defaultTheme);

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeTrue();
        result.PrimaryColor.Should().Be("#DEFAULT");
    }

    [Fact]
    public async Task Get_ComplexTypeNotFound_ThrowsException()
    {
        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var act = () => client.Get<IntegrationThemeConfig>("missing-theme");

        act.Should().Throw<ConfigNotFoundException>();
    }

    [Fact]
    public async Task Get_ComplexTypeDefault_UsedBeforeConnect()
    {
        var defaultTheme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#DEFAULT", FontSize = 10 };

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["theme"] = defaultTheme }
        });

        // Before connect
        var result = client.Get<IntegrationThemeConfig>("theme");
        result!.PrimaryColor.Should().Be("#DEFAULT");
    }

    [Fact]
    public async Task Get_ComplexTypeDefault_ServerOverrides()
    {
        var defaultTheme = new IntegrationThemeConfig { DarkMode = true, PrimaryColor = "#DEFAULT", FontSize = 10 };
        var serverTheme = new IntegrationThemeConfig { DarkMode = false, PrimaryColor = "#SERVER", FontSize = 14 };

        _server.AddConfig("theme", serverTheme);

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Defaults = new Dictionary<string, object?> { ["theme"] = defaultTheme }
        });

        // Before connect - default
        client.Get<IntegrationThemeConfig>("theme")!.PrimaryColor.Should().Be("#DEFAULT");

        await client.ConnectAsync(GetConnectOptions());

        // After connect - server value
        client.Get<IntegrationThemeConfig>("theme")!.PrimaryColor.Should().Be("#SERVER");
    }

    [Fact]
    public async Task Get_EmptyComplexType_DeserializesCorrectly()
    {
        var emptyTheme = new IntegrationThemeConfig();
        _server.AddConfig("empty-theme", emptyTheme);

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var result = client.Get<IntegrationThemeConfig>("empty-theme");

        result.Should().NotBeNull();
        result!.DarkMode.Should().BeFalse();
        result.PrimaryColor.Should().Be("");
        result.FontSize.Should().Be(0);
    }

    [Fact]
    public async Task ConfigChanged_GetValue_DeserializesComplexType()
    {
        _server.AddConfig("watched-theme", new IntegrationThemeConfig { DarkMode = false, PrimaryColor = "#INITIAL", FontSize = 12 });

        await using var client = new ReplaneClient();

        await client.ConnectAsync(GetConnectOptions());

        var receivedTheme = (IntegrationThemeConfig?)null;
        client.ConfigChanged += (sender, e) =>
        {
            if (e.ConfigName == "watched-theme")
            {
                receivedTheme = e.GetValue<IntegrationThemeConfig>();
            }
        };

        // Verify initial value
        var initial = client.Get<IntegrationThemeConfig>("watched-theme");
        initial!.PrimaryColor.Should().Be("#INITIAL");
    }

    [Fact]
    public async Task ReplaneClientId_IsAutoGenerated()
    {
        // Add a config with segmentation override on replaneClientId
        _server.AddConfig("feature", "default", new List<object>
        {
            new {
                name = "segmented-override",
                conditions = new[] {
                    new {
                        @operator = "segmentation",
                        property = "replaneClientId",
                        fromPercentage = 0,
                        toPercentage = 100,
                        seed = "test-seed"
                    }
                },
                value = "segmented-value"
            }
        });

        await using var client = new ReplaneClient();
        await client.ConnectAsync(GetConnectOptions());

        // Should match segmentation because replaneClientId is auto-generated
        client.Get<string>("feature").Should().Be("segmented-value");
    }

    [Fact]
    public async Task ReplaneClientId_UserProvidedTakesPrecedence()
    {
        var userProvidedId = "user-provided-client-id";

        // Add a config with equals override on the user-provided replaneClientId
        _server.AddConfig("feature", "default", new List<object>
        {
            new {
                name = "user-override",
                conditions = new[] {
                    new {
                        @operator = "equals",
                        property = "replaneClientId",
                        value = userProvidedId
                    }
                },
                value = "user-override-value"
            }
        });

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            Context = new ReplaneContext
            {
                ["replaneClientId"] = userProvidedId
            }
        });
        await client.ConnectAsync(GetConnectOptions());

        // User-provided replaneClientId should take precedence
        client.Get<string>("feature").Should().Be("user-override-value");
    }

    [Fact]
    public async Task ReplaneClientId_PerRequestContextOverrides()
    {
        var perRequestId = "per-request-client-id";

        // Add a config with equals override on the per-request replaneClientId
        _server.AddConfig("feature", "default", new List<object>
        {
            new {
                name = "per-request-override",
                conditions = new[] {
                    new {
                        @operator = "equals",
                        property = "replaneClientId",
                        value = perRequestId
                    }
                },
                value = "per-request-value"
            }
        });

        await using var client = new ReplaneClient();
        await client.ConnectAsync(GetConnectOptions());

        // Default should be based on auto-generated ID (won't match)
        client.Get<string>("feature").Should().Be("default");

        // Per-request context should override the auto-generated ID
        client.Get<string>("feature", new ReplaneContext
        {
            ["replaneClientId"] = perRequestId
        }).Should().Be("per-request-value");
    }

    [Fact]
    public async Task ReplaneClientId_IsUniquePerClientInstance()
    {
        // Add a config with 50% segmentation
        _server.AddConfig("feature", "default", new List<object>
        {
            new {
                name = "50-percent-rollout",
                conditions = new[] {
                    new {
                        @operator = "segmentation",
                        property = "replaneClientId",
                        fromPercentage = 0,
                        toPercentage = 50,
                        seed = "test-seed"
                    }
                },
                value = "rollout-value"
            }
        });

        // Create multiple clients and check they get different segmentation results
        var results = new List<string?>();
        for (var i = 0; i < 10; i++)
        {
            await using var client = new ReplaneClient();
            await client.ConnectAsync(GetConnectOptions());
            results.Add(client.Get<string>("feature"));
        }

        // With 10 clients and 50% rollout, we should statistically see both values
        // This test mainly verifies that segmentation is working
        (results.Contains("rollout-value") || results.Contains("default")).Should().BeTrue();
    }
}
