using System.Reflection;

namespace Replane.Tests;

public class SdkMetadataTests
{
    private static string GetCleanVersion()
    {
        var version = typeof(ReplaneClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ReplaneClient).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        // Strip git commit hash suffix (e.g., "0.1.0+abc123" -> "0.1.0")
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    [Fact]
    public void SdkVersion_IsExtractedFromAssembly()
    {
        var version = GetCleanVersion();

        version.Should().NotBeNullOrEmpty();
        version.Should().NotBe("unknown");
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SdkVersion_IsValidSemver()
    {
        var version = GetCleanVersion();

        // Should be a valid semver format (major.minor.patch)
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SdkVersion_DoesNotContainGitHash()
    {
        var version = GetCleanVersion();

        // Should not contain the + suffix with git hash
        version.Should().NotContain("+");
    }
}
