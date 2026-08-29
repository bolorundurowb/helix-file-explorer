using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class PathUtilitiesTests
{
    [Theory]
    [InlineData("", PathKind.Empty)]
    [InlineData("__home__", PathKind.Home)]
    [InlineData(@"C:\Users", PathKind.Physical)]
    [InlineData(@"C:\Users\", PathKind.Physical)]
    [InlineData(@"C:/Users", PathKind.Physical)]
    [InlineData(@"\\", PathKind.Unc)]
    [InlineData(@"\\server", PathKind.Unc)]
    [InlineData(@"\\server\share", PathKind.Unc)]
    [InlineData(@"\\server\share\folder", PathKind.Unc)]
    [InlineData("shell:RecycleBinFolder", PathKind.RecycleBin)]
    [InlineData("shell:Downloads", PathKind.Shell)]
    [InlineData("archive://C:\\backup.zip!", PathKind.Archive)]
    [InlineData("archive://C:\\backup.zip!docs/readme.txt", PathKind.Archive)]
    public void Classify_IdentifiesPathKind(string path, PathKind expected)
    {
        PathUtilities.Classify(path).Must().Be(expected);
    }

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
    public void IsSameOrChildPath_PhysicalPaths(string directory, string path, bool expected)
    {
        PathUtilities.IsSameOrChildPath(directory, path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share", @"\\server\share", true)]
    [InlineData(@"\\server\share", @"\\server\share\folder", true)]
    [InlineData(@"\\server\share", @"\\server\sharefolder", false)]
    [InlineData(@"\\server\share", @"\\other\share\folder", false)]
    public void IsSameOrChildPath_UncPaths(string directory, string path, bool expected)
    {
        PathUtilities.IsSameOrChildPath(directory, path).Must().Be(expected);
    }

    [Theory]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!docs/", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("archive://C:\\backup.zip!docs/", "archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("archive://C:\\backup.zip!", "archive://C:\\other.zip!docs/", false)]
    public void IsSameOrChildPath_ArchivePaths(string directory, string path, bool expected)
    {
        PathUtilities.IsSameOrChildPath(directory, path).Must().Be(expected);
    }

    [Fact]
    public void IsSameOrChildPath_DifferentPathKinds_ReturnsFalse()
    {
        PathUtilities.IsSameOrChildPath(@"C:\folder", "archive://C:\\folder.zip!").Must().BeFalse();
        PathUtilities.IsSameOrChildPath(@"C:\folder", "shell:Downloads").Must().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\", @"C:\", true)]
    [InlineData(@"C:\", @"C:\folder", true)]
    [InlineData(@"C:", @"C:\folder", true)]
    [InlineData(@"C:\", @"C:\foo", true)]
    public void IsSameOrChildPath_DriveRoot(string directory, string path, bool expected)
    {
        PathUtilities.IsSameOrChildPath(directory, path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\foo", @"C:\foo", true)]
    [InlineData(@"C:\foo\", @"C:/foo", true)]
    [InlineData(@"C:\foo\bar", @"C:\foo\bar\baz\..", true)]
    [InlineData(@"C:\foo\bar", @"C:\foo\bar\\baz\\..", true)]
    public void PathsEqual_PhysicalPaths(string a, string b, bool expected)
    {
        PathUtilities.PathsEqual(a, b).Must().Be(expected);
    }

    [Fact]
    public void NormalizePath_ResolvesRelativeSegments()
    {
        var normalized = PathUtilities.NormalizePath(@"C:\foo\bar\..\baz");
        normalized.Must().Be(@"C:\foo\baz");
    }

    [Fact]
    public void NormalizePath_PreservesDriveRoot()
    {
        var normalized = PathUtilities.NormalizePath(@"C:\");
        normalized.Must().Be(@"C:\");
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:", true)]
    [InlineData(@"C:\folder", false)]
    [InlineData(@"\\server\share", false)]
    public void IsDriveRoot_IdentifiesDriveRoots(string path, bool expected)
    {
        PathUtilities.IsDriveRoot(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server", true)]
    [InlineData(@"\\", true)]
    [InlineData(@"C:\folder", false)]
    public void IsUncPath_IdentifiesUncPaths(string path, bool expected)
    {
        PathUtilities.IsUncPath(path).Must().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\a", @"C:\b", true)]
    [InlineData(@"C:\a", @"D:\b", false)]
    [InlineData(@"C:\a", @"\\server\share\folder", false)]
    [InlineData(@"\\server\share\a", @"\\server\share\b", true)]
    public void IsSameVolume_compares_path_roots(string source, string destination, bool expected)
    {
        PathUtilities.IsSameVolume(source, destination).Must().Be(expected);
    }

    [Fact]
    public void IsSameVolume_empty_is_false()
    {
        PathUtilities.IsSameVolume("", @"C:\").Must().BeFalse();
        PathUtilities.IsSameVolume(@"C:\", "").Must().BeFalse();
    }
}
