using System.Reflection;

namespace Replane.Tests;

public class SdkMetadataTests
{
    [Fact]
    public void SdkVersion_IsExtractedFromAssembly()
    {
        // Get the version the same way ReplaneClient does
        var assembly = typeof(ReplaneClient).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        version.Should().NotBeNullOrEmpty();
        version.Should().NotBe("unknown");
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void SdkVersion_MatchesCsprojVersion()
    {
        var assembly = typeof(ReplaneClient).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();

        // The version in .csproj is 0.1.0
        version.Should().StartWith("0.1.0");
    }
}
