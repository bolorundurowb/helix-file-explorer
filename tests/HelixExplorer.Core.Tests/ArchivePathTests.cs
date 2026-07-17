using HelixExplorer.Core.Archives;

namespace HelixExplorer.Core.Tests;

public sealed class ArchivePathTests
{
    [Theory]
    [InlineData("archive://C:\\backup.zip!", true)]
    [InlineData("archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("C:\\backup.zip", false)]
    [InlineData("", false)]
    public void IsVirtual_detects_scheme(string path, bool expected)
    {
        ArchivePath.IsVirtual(path).Must().Be(expected);
    }

    [Fact]
    public void Mount_wraps_physical_archive_path()
    {
        ArchivePath.Mount(@"C:\backup.zip").Must().Be("archive://C:\\backup.zip!");
    }

    [Fact]
    public void TryParse_splits_host_and_inner()
    {
        ArchivePath.TryParse(@"archive://C:\backup.zip!docs/readme.txt", out var host, out var inner).Must().BeTrue();
        host.Must().Be(@"C:\backup.zip");
        inner.Must().Be("docs/readme.txt");
    }

    [Fact]
    public void GetParent_at_root_returns_physical_archive_folder()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!");
        parent.Must().Be(@"C:\backup.zip\");
    }

    [Fact]
    public void GetParent_from_nested_inner_path_trims_segment()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!docs/sub/file.txt");
        parent.Must().Be("archive://C:\\backup.zip!docs/sub/");
    }

    [Fact]
    public void GetBreadcrumbs_builds_archive_segments()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/sub/");
        crumbs.Count.Must().Be(3);
        crumbs[0].DisplayName.Must().Be("backup.zip");
        crumbs[1].DisplayName.Must().Be("docs");
        crumbs[2].DisplayName.Must().Be("sub");
        crumbs[2].IsLast.Must().BeTrue();
    }

    [Fact]
    public void GetBreadcrumbs_paths_have_no_double_slash()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/sub/");
        crumbs.Count.Must().Be(3);
        crumbs[0].Path.Must().Be("archive://C:\\backup.zip!");
        crumbs[1].Path.Must().Be("archive://C:\\backup.zip!docs/");
        crumbs[2].Path.Must().Be("archive://C:\\backup.zip!docs/sub/");

        foreach (var crumb in crumbs)
        {
            var afterScheme = crumb.Path.StartsWith("archive://", StringComparison.OrdinalIgnoreCase)
                ? crumb.Path["archive://".Length..]
                : crumb.Path;
            afterScheme.Must().NotContain("//");
        }
    }

    [Fact]
    public void GetBreadcrumbs_file_at_leaf_has_no_trailing_slash()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/file.txt");
        crumbs.Count.Must().Be(3);
        crumbs[2].IsLast.Must().BeTrue();
        crumbs[2].Path.Must().Be("archive://C:\\backup.zip!docs/file.txt");
    }

    [Fact]
    public void Mount_escapes_exclamation_in_host_path()
    {
        var mounted = ArchivePath.Mount(@"C:\my!folder\archive.zip");
        mounted.Must().Be("archive://C:\\my%21folder\\archive.zip!");
    }

    [Fact]
    public void TryParse_unescapes_exclamation_in_host_path()
    {
        ArchivePath.TryParse(
            @"archive://C:\my%21folder\archive.zip!inner/path",
            out var host,
            out var inner).Must().BeTrue();
        host.Must().Be(@"C:\my!folder\archive.zip");
        inner.Must().Be("inner/path");
    }

    [Fact]
    public void TryParse_allows_literal_exclamation_in_inner_path()
    {
        ArchivePath.TryParse(
            @"archive://C:\backup.zip!docs/weird!name.txt",
            out var host,
            out var inner).Must().BeTrue();
        host.Must().Be(@"C:\backup.zip");
        inner.Must().Be("docs/weird!name.txt");
    }

    [Fact]
    public void Combine_handles_exclamation_in_host_and_inner()
    {
        var path = ArchivePath.Combine(@"C:\my!archive\test.zip", "a!b/c.txt");
        ArchivePath.TryParse(path, out var host, out var inner).Must().BeTrue();
        host.Must().Be(@"C:\my!archive\test.zip");
        inner.Must().Be("a!b/c.txt");
    }

    [Fact]
    public void Mount_and_parse_roundtrip_with_exclamation_in_host()
    {
        var hostFile = @"C:\my!archive\test.zip";
        var mounted = ArchivePath.Mount(hostFile);
        ArchivePath.TryParse(mounted + "docs/readme.txt", out var parsedHost, out var parsedInner).Must().BeTrue();
        parsedHost.Must().Be(hostFile);
        parsedInner.Must().Be("docs/readme.txt");
    }

    [Fact]
    public void EscapeHost_encodes_percent_before_exclamation()
    {
        ArchivePath.EscapeHost(@"C:\a%21b!c.zip").Must().Be(@"C:\a%2521b%21c.zip");
        ArchivePath.UnescapeHost(@"C:\a%2521b%21c.zip").Must().Be(@"C:\a%21b!c.zip");
    }
}
