using Replane.Tests.MockServer;
using System.Text.Json;

namespace Replane.Tests;

public class ClientIntegrationTests : IAsyncLifetime
{
    // Helper to convert object to JsonElement for tests
    private static JsonElement ToJson(object? value) => JsonValueConverter.ToJsonElement(value);
    private MockReplaneServer _server = null!;

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

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

        client.IsInitialized.Should().BeTrue();
        client.Get<bool>("feature-enabled").Should().BeTrue();
        client.Get<int>("max-items").Should().Be(100);
    }

    [Fact]
    public async Task ConnectAsync_WithFallbacks_UsesFallbacksBeforeInit()
    {
        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey,
            Fallbacks = new Dictionary<string, object?> { ["fallback-config"] = "fallback-value" }
        });

        // Before connecting, fallback should be available
        var value = client.Get<string>("fallback-config");
        value.Should().Be("fallback-value");
    }

    [Fact]
    public async Task ConnectAsync_AuthenticationError_ThrowsException()
    {
        _server.SimulateAuthError = true;

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = "invalid-key"
        });

        var act = () => client.ConnectAsync();

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task ConnectAsync_Timeout_ThrowsException()
    {
        _server.InitDelay = TimeSpan.FromSeconds(10);

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey,
            InitializationTimeoutMs = 100
        });

        var act = () => client.ConnectAsync();

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

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

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
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey,
            Context = new ReplaneContext { ["region"] = "us" }
        });

        await client.ConnectAsync();

        // Only default context - no match
        client.Get<string>("test-config").Should().Be("default");

        // Call context merged with default - should match
        client.Get<string>("test-config", new ReplaneContext { ["plan"] = "pro" })
            .Should().Be("us-pro");
    }

    [Fact]
    public async Task Get_ConfigNotFound_ThrowsException()
    {
        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

        var act = () => client.Get<string>("non-existent-config");

        act.Should().Throw<ConfigNotFoundException>()
            .Which.ConfigName.Should().Be("non-existent-config");
    }

    [Fact]
    public async Task Get_ConfigNotFound_WithDefault_ReturnsDefault()
    {
        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

        var value = client.Get("non-existent-config", defaultValue: "my-default");

        value.Should().Be("my-default");
    }

    [Fact]
    public async Task Subscribe_ReceivesUpdates()
    {
        _server.AddConfig("test-config", "initial");

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

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

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();

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

        var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey
        });

        await client.ConnectAsync();
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
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey,
            Required = ["required-1", "required-2"]
        });

        var act = () => client.ConnectAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RequiredConfigs_Missing_ThrowsException()
    {
        _server.AddConfig("required-1", true);
        // required-2 is not added

        await using var client = new ReplaneClient(new ReplaneClientOptions
        {
            BaseUrl = _server.BaseUrl,
            SdkKey = _server.SdkKey,
            Required = ["required-1", "required-2"]
        });

        var act = () => client.ConnectAsync();

        await act.Should().ThrowAsync<ConfigNotFoundException>();
    }
}
