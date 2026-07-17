using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Tests;

public class NetworkPathTests
{
    [Theory]
    [InlineData(@"\\", true)]
    [InlineData(@"\\server", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("//server/share", true)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"\single", false)]
    [InlineData("", false)]
    public void IsUnc_DetectsUncPaths(string path, bool expected)
    {
        NetworkPath.IsUnc(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\", true)]
    [InlineData(@"\\\\", true)]
    [InlineData(@"\\server", false)]
    [InlineData(@"C:\", false)]
    public void IsNetworkRoot_OnlyBareRoot(string path, bool expected)
    {
        NetworkPath.IsNetworkRoot(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData("//server/share", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\server\\share", @"\\server\share")]
    [InlineData(@"  \\server\share  ", @"\\server\share")]
    [InlineData(@"\\", @"\\")]
    [InlineData(@"C:\Windows", @"C:\Windows")]
    public void Normalize_ProducesCanonicalUnc(string input, string expected)
    {
        NetworkPath.Normalize(input).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share", "server")]
    [InlineData(@"\\server", "server")]
    [InlineData("//server/share/sub", "server")]
    [InlineData(@"\\", null)]
    [InlineData(@"C:\x", null)]
    public void GetServer_ExtractsServer(string path, string? expected)
    {
        NetworkPath.GetServer(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"\\server\share\sub", "share")]
    [InlineData(@"\\server", null)]
    [InlineData(@"\\", null)]
    public void GetShare_ExtractsShare(string path, string? expected)
    {
        NetworkPath.GetShare(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server", true)]
    [InlineData(@"\\server\", true)]
    [InlineData(@"\\server\share", false)]
    [InlineData(@"\\", false)]
    public void IsServerRoot_DetectsServerWithoutShare(string path, bool expected)
    {
        NetworkPath.IsServerRoot(path).Must().Be(expected);
    }

    [Fact]
    public void ForServer_BuildsUncRoot()
    {
        NetworkPath.ForServer("nas").Must().Be(@"\\nas");
        NetworkPath.ForServer(@"\\nas").Must().Be(@"\\nas");
    }

    [Fact]
    public void Deduplicate_RemovesCaseInsensitiveDuplicatesAndSorts()
    {
        var input = new[]
        {
            new NetworkLocationInfo(@"\\SERVER\Share", "Zeta"),
            new NetworkLocationInfo(@"\\server\share", "Alpha"),
            new NetworkLocationInfo(@"\\other", "Middle"),
            new NetworkLocationInfo("", "Blank"),
        };

        var result = NetworkPath.Deduplicate(input);

        result.Count.Must().Be(2);
        result[0].DisplayName.Must().Be("Middle");
        result[1].DisplayName.Must().Be("Zeta");
    }
}

public class NetworkDiscoveryResultTests
{
    [Fact]
    public void From_EmptyList_IsNoLocationsFound()
    {
        var result = NetworkDiscoveryResult.From(Array.Empty<NetworkLocationInfo>());
        result.Status.Must().Be(NetworkDiscoveryStatus.NoLocationsFound);
    }

    [Fact]
    public void From_NonEmpty_IsDiscovered()
    {
        var result = NetworkDiscoveryResult.From(new[] { new NetworkLocationInfo(@"\\a", "a") });
        result.Status.Must().Be(NetworkDiscoveryStatus.Discovered);
    }

    [Fact]
    public void Failed_MarksFailure()
    {
        NetworkDiscoveryResult.Failed().Status.Must().Be(NetworkDiscoveryStatus.DiscoveryFailed);
    }
}
