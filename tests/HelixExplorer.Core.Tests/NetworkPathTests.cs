using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Xunit;

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
        Assert.Equal(expected, NetworkPath.IsUnc(path));
    }

    [Theory]
    [InlineData(@"\\", true)]
    [InlineData(@"\\\\", true)]
    [InlineData(@"\\server", false)]
    [InlineData(@"C:\", false)]
    public void IsNetworkRoot_OnlyBareRoot(string path, bool expected)
    {
        Assert.Equal(expected, NetworkPath.IsNetworkRoot(path));
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
        Assert.Equal(expected, NetworkPath.Normalize(input));
    }

    [Theory]
    [InlineData(@"\\server\share", "server")]
    [InlineData(@"\\server", "server")]
    [InlineData("//server/share/sub", "server")]
    [InlineData(@"\\", null)]
    [InlineData(@"C:\x", null)]
    public void GetServer_ExtractsServer(string path, string? expected)
    {
        Assert.Equal(expected, NetworkPath.GetServer(path));
    }

    [Theory]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"\\server\share\sub", "share")]
    [InlineData(@"\\server", null)]
    [InlineData(@"\\", null)]
    public void GetShare_ExtractsShare(string path, string? expected)
    {
        Assert.Equal(expected, NetworkPath.GetShare(path));
    }

    [Theory]
    [InlineData(@"\\server", true)]
    [InlineData(@"\\server\", true)]
    [InlineData(@"\\server\share", false)]
    [InlineData(@"\\", false)]
    public void IsServerRoot_DetectsServerWithoutShare(string path, bool expected)
    {
        Assert.Equal(expected, NetworkPath.IsServerRoot(path));
    }

    [Fact]
    public void ForServer_BuildsUncRoot()
    {
        Assert.Equal(@"\\nas", NetworkPath.ForServer("nas"));
        Assert.Equal(@"\\nas", NetworkPath.ForServer(@"\\nas"));
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

        Assert.Equal(2, result.Count);
        // Case-insensitive path collision keeps the first occurrence (Zeta), not later Alpha.
        Assert.Equal("Middle", result[0].DisplayName);
        Assert.Equal("Zeta", result[1].DisplayName);
    }
}

public class NetworkDiscoveryResultTests
{
    [Fact]
    public void From_EmptyList_IsNoLocationsFound()
    {
        var result = NetworkDiscoveryResult.From(Array.Empty<NetworkLocationInfo>());
        Assert.Equal(NetworkDiscoveryStatus.NoLocationsFound, result.Status);
    }

    [Fact]
    public void From_NonEmpty_IsDiscovered()
    {
        var result = NetworkDiscoveryResult.From(new[] { new NetworkLocationInfo(@"\\a", "a") });
        Assert.Equal(NetworkDiscoveryStatus.Discovered, result.Status);
    }

    [Fact]
    public void Failed_MarksFailure()
    {
        Assert.Equal(NetworkDiscoveryStatus.DiscoveryFailed, NetworkDiscoveryResult.Failed().Status);
    }
}
