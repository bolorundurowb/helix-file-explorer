using HelixExplorer.Core.FileSystem;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class PathUtilitiesTests
{
    [Theory]
    [InlineData("", PathKind.Empty)]
    [InlineData("__home__", PathKind.Home)]
    [InlineData(@"C:\Users", PathKind.Physical)]
    [InlineData(@"C:\Users\", PathKind.Physical)]
    [InlineData(@"C:/Users", PathKind.Physical)]
    [InlineData(@"\\server\share", PathKind.Unc)]
    [InlineData(@"\\server\share\folder", PathKind.Unc)]
    [InlineData("shell:RecycleBinFolder", PathKind.RecycleBin)]
    [InlineData("shell:Downloads", PathKind.Shell)]
    [InlineData("archive://C:\\backup.zip!", PathKind.Archive)]
    [InlineData("archive://C:\\backup.zip!docs/readme.txt", PathKind.Archive)]
    public void Classify_identifies_path_kind(string path, PathKind expected)
        => Assert.Equal(expected, PathUtilities.Classify(path));

    [Theory]
    [InlineData(@"C:\foo", @"C:\foo", true)]
    [InlineData(@"C:\foo", @"C:\foo\bar", true)]
    [InlineData(@"C:\foo", @"C:\foo\bar\baz", true)]
    [InlineData(@"C:\foo", @"C:\foobar", false)]
    [InlineData(@"C:\foo", @"C:\bar", false)]
    [InlineData(@"C:\foo", @"D:\foo\bar", false)]
    [InlineData(@"C:\foo\", @"C:\foo\bar", true)]
    [InlineData(@"C:/foo", @"C:\foo\bar", true)]
    [InlineData(@"C:\foo\bar", @"C:\foo", false)]
    [InlineData(@"C:\foo", @"C:\foo\..\bar", false)]
    [InlineData(@"C:\foo", @"C:\foo\bar\..", true)]
    public void IsSameOrChildPath_handles_physical_paths(string directory, string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsSameOrChildPath(directory, path));

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share", true)]
    [InlineData(@"\\server\share", @"\\server\share\folder", true)]
    [InlineData(@"\\server\share", @"\\server\sharefolder", false)]
    [InlineData(@"\\server\share", @"\\other\share\folder", false)]
    public void IsSameOrChildPath_handles_unc_paths(string directory, string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsSameOrChildPath(directory, path));

    [Theory]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!docs/", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("archive://C:\\backup.zip!docs/", "archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\other.zip!docs/", false)]
    public void IsSameOrChildPath_handles_archive_paths(string directory, string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsSameOrChildPath(directory, path));

    [Fact]
    public void IsSameOrChildPath_different_kinds_returns_false()
    {
        Assert.False(PathUtilities.IsSameOrChildPath(@"C:\folder", "archive://C:\\folder.zip!"));
        Assert.False(PathUtilities.IsSameOrChildPath(@"C:\folder", "shell:Downloads"));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\", true)]
    [InlineData(@"C:\", @"C:\folder", true)]
    [InlineData(@"C:", @"C:\folder", true)]
    [InlineData(@"C:\", @"C:\foo", true)]
    public void IsSameOrChildPath_drive_root(string directory, string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsSameOrChildPath(directory, path));

    [Theory]
    [InlineData(@"C:\foo", @"C:\foo", true)]
    [InlineData(@"C:\foo\", @"C:/foo", true)]
    [InlineData(@"C:\foo\bar", @"C:\foo\bar\baz\..", true)]
    [InlineData(@"C:\foo\bar", @"C:\foo\bar\\baz\\..", true)]
    public void PathsEqual_handles_physical_paths(string a, string b, bool expected)
        => Assert.Equal(expected, PathUtilities.PathsEqual(a, b));

    [Fact]
    public void NormalizePath_resolves_relative_segments()
    {
        var normalized = PathUtilities.NormalizePath(@"C:\foo\bar\..\baz");
        Assert.Equal(@"C:\foo\baz", normalized);
    }

    [Fact]
    public void NormalizePath_preserves_drive_root()
    {
        var normalized = PathUtilities.NormalizePath(@"C:\");
        Assert.Equal(@"C:\", normalized);
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:", true)]
    [InlineData(@"C:\folder", false)]
    [InlineData(@"\\server\share", false)]
    public void IsDriveRoot_identifies_drive_roots(string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsDriveRoot(path));

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server", false)]
    [InlineData(@"C:\folder", false)]
    public void IsUncPath_identifies_unc_paths(string path, bool expected)
        => Assert.Equal(expected, PathUtilities.IsUncPath(path));
}
