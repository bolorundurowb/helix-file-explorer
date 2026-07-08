using HelixExplorer.Core.Archives;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class ArchivePathTests
{
    [Theory]
    [InlineData("archive://C:\\backup.zip!", true)]
    [InlineData("archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("C:\\backup.zip", false)]
    [InlineData("", false)]
    public void IsVirtual_detects_scheme(string path, bool expected)
        => Assert.Equal(expected, ArchivePath.IsVirtual(path));

    [Fact]
    public void Mount_wraps_physical_archive_path()
        => Assert.Equal("archive://C:\\backup.zip!", ArchivePath.Mount(@"C:\backup.zip"));

    [Fact]
    public void TryParse_splits_host_and_inner()
    {
        Assert.True(ArchivePath.TryParse(@"archive://C:\backup.zip!docs/readme.txt", out var host, out var inner));
        Assert.Equal(@"C:\backup.zip", host);
        Assert.Equal("docs/readme.txt", inner);
    }

    [Fact]
    public void GetParent_at_root_returns_physical_archive_folder()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!");
        Assert.Equal(@"C:\backup.zip\", parent);
    }

    [Fact]
    public void GetParent_from_nested_inner_path_trims_segment()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!docs/sub/file.txt");
        Assert.Equal("archive://C:\\backup.zip!docs/sub/", parent);
    }

    [Fact]
    public void GetBreadcrumbs_builds_archive_segments()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/sub/");
        Assert.Equal(3, crumbs.Count);
        Assert.Equal("backup.zip", crumbs[0].DisplayName);
        Assert.Equal("docs", crumbs[1].DisplayName);
        Assert.Equal("sub", crumbs[2].DisplayName);
        Assert.True(crumbs[2].IsLast);
    }
}
