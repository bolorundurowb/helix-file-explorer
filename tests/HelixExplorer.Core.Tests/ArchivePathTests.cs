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

    [Fact]
    public void GetBreadcrumbs_paths_have_no_double_slash()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/sub/");
        Assert.Equal(3, crumbs.Count);
        Assert.Equal("archive://C:\\backup.zip!", crumbs[0].Path);
        Assert.Equal("archive://C:\\backup.zip!docs/", crumbs[1].Path);
        Assert.Equal("archive://C:\\backup.zip!docs/sub/", crumbs[2].Path);

        foreach (var crumb in crumbs)
        {
            var afterScheme = crumb.Path.StartsWith("archive://", StringComparison.OrdinalIgnoreCase)
                ? crumb.Path["archive://".Length..]
                : crumb.Path;
            Assert.DoesNotContain("//", afterScheme);
        }
    }

    [Fact]
    public void GetBreadcrumbs_file_at_leaf_has_no_trailing_slash()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/file.txt");
        Assert.Equal(3, crumbs.Count);
        Assert.True(crumbs[2].IsLast);
        Assert.Equal("archive://C:\\backup.zip!docs/file.txt", crumbs[2].Path);
    }

    [Fact]
    public void Mount_escapes_exclamation_in_host_path()
    {
        var mounted = ArchivePath.Mount(@"C:\my!folder\archive.zip");
        Assert.Equal("archive://C:\\my%21folder\\archive.zip!", mounted);
    }

    [Fact]
    public void TryParse_unescapes_exclamation_in_host_path()
    {
        Assert.True(ArchivePath.TryParse(
            @"archive://C:\my%21folder\archive.zip!inner/path",
            out var host,
            out var inner));
        Assert.Equal(@"C:\my!folder\archive.zip", host);
        Assert.Equal("inner/path", inner);
    }

    [Fact]
    public void TryParse_allows_literal_exclamation_in_inner_path()
    {
        Assert.True(ArchivePath.TryParse(
            @"archive://C:\backup.zip!docs/weird!name.txt",
            out var host,
            out var inner));
        Assert.Equal(@"C:\backup.zip", host);
        Assert.Equal("docs/weird!name.txt", inner);
    }

    [Fact]
    public void Combine_handles_exclamation_in_host_and_inner()
    {
        var path = ArchivePath.Combine(@"C:\my!archive\test.zip", "a!b/c.txt");
        Assert.True(ArchivePath.TryParse(path, out var host, out var inner));
        Assert.Equal(@"C:\my!archive\test.zip", host);
        Assert.Equal("a!b/c.txt", inner);
    }

    [Fact]
    public void Mount_and_parse_roundtrip_with_exclamation_in_host()
    {
        var hostFile = @"C:\my!archive\test.zip";
        var mounted = ArchivePath.Mount(hostFile);
        Assert.True(ArchivePath.TryParse(mounted + "docs/readme.txt", out var parsedHost, out var parsedInner));
        Assert.Equal(hostFile, parsedHost);
        Assert.Equal("docs/readme.txt", parsedInner);
    }

    [Fact]
    public void EscapeHost_encodes_percent_before_exclamation()
    {
        Assert.Equal(@"C:\a%2521b%21c.zip", ArchivePath.EscapeHost(@"C:\a%21b!c.zip"));
        Assert.Equal(@"C:\a%21b!c.zip", ArchivePath.UnescapeHost(@"C:\a%2521b%21c.zip"));
    }
}
