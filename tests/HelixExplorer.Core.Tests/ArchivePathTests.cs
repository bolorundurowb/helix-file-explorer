using HelixExplorer.Core.Archives;

namespace HelixExplorer.Core.Tests;

public sealed class ArchivePathTests
{
    [Theory]
    [InlineData("archive://C:\\backup.zip!", true)]
    [InlineData("archive://C:\\backup.zip!docs/readme.txt", true)]
    [InlineData("C:\\backup.zip", false)]
    [InlineData("", false)]
    public void IsVirtual_DetectsScheme(string path, bool expected)
    {
        ArchivePath.IsVirtual(path).Must().Be(expected);
    }

    [Fact]
    public void Mount_WrapsPhysicalArchivePath()
    {
        ArchivePath.Mount(@"C:\backup.zip").Must().Be("archive://C:\\backup.zip!");
    }

    [Fact]
    public void TryParse_SplitsHostAndInner()
    {
        ArchivePath.TryParse(@"archive://C:\backup.zip!docs/readme.txt", out var host, out var inner).Must().BeTrue();
        host.Must().Be(@"C:\backup.zip");
        inner.Must().Be("docs/readme.txt");
    }

    [Fact]
    public void GetParent_AtRoot_ReturnsPhysicalArchiveFolder()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!");
        parent.Must().Be(@"C:\backup.zip\");
    }

    [Fact]
    public void GetParent_FromNestedInnerPath_TrimsSegment()
    {
        var parent = ArchivePath.GetParent("archive://C:\\backup.zip!docs/sub/file.txt");
        parent.Must().Be("archive://C:\\backup.zip!docs/sub/");
    }

    [Fact]
    public void GetBreadcrumbs_BuildsArchiveSegments()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/sub/");
        crumbs.Count.Must().Be(3);
        crumbs[0].DisplayName.Must().Be("backup.zip");
        crumbs[1].DisplayName.Must().Be("docs");
        crumbs[2].DisplayName.Must().Be("sub");
        crumbs[2].IsLast.Must().BeTrue();
    }

    [Fact]
    public void GetBreadcrumbs_PathsHaveNoDoubleSlash()
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
    public void GetBreadcrumbs_FileAtLeaf_HasNoTrailingSlash()
    {
        var crumbs = ArchivePath.GetBreadcrumbs("archive://C:\\backup.zip!docs/file.txt");
        crumbs.Count.Must().Be(3);
        crumbs[2].IsLast.Must().BeTrue();
        crumbs[2].Path.Must().Be("archive://C:\\backup.zip!docs/file.txt");
    }

    [Fact]
    public void Mount_EscapesExclamationInHostPath()
    {
        var mounted = ArchivePath.Mount(@"C:\my!folder\archive.zip");
        mounted.Must().Be("archive://C:\\my%21folder\\archive.zip!");
    }

    [Fact]
    public void TryParse_UnescapesExclamationInHostPath()
    {
        ArchivePath.TryParse(
            @"archive://C:\my%21folder\archive.zip!inner/path",
            out var host,
            out var inner).Must().BeTrue();
        host.Must().Be(@"C:\my!folder\archive.zip");
        inner.Must().Be("inner/path");
    }

    [Fact]
    public void TryParse_AllowsLiteralExclamationInInnerPath()
    {
        ArchivePath.TryParse(
            @"archive://C:\backup.zip!docs/weird!name.txt",
            out var host,
            out var inner).Must().BeTrue();
        host.Must().Be(@"C:\backup.zip");
        inner.Must().Be("docs/weird!name.txt");
    }

    [Fact]
    public void Combine_HandlesExclamationInHostAndInner()
    {
        var path = ArchivePath.Combine(@"C:\my!archive\test.zip", "a!b/c.txt");
        ArchivePath.TryParse(path, out var host, out var inner).Must().BeTrue();
        host.Must().Be(@"C:\my!archive\test.zip");
        inner.Must().Be("a!b/c.txt");
    }

    [Fact]
    public void MountThenTryParse_RoundtripsExclamationInHost()
    {
        var hostFile = @"C:\my!archive\test.zip";
        var mounted = ArchivePath.Mount(hostFile);
        ArchivePath.TryParse(mounted + "docs/readme.txt", out var parsedHost, out var parsedInner).Must().BeTrue();
        parsedHost.Must().Be(hostFile);
        parsedInner.Must().Be("docs/readme.txt");
    }

    [Fact]
    public void EscapeHost_EncodesPercentBeforeExclamation()
    {
        ArchivePath.EscapeHost(@"C:\a%21b!c.zip").Must().Be(@"C:\a%2521b%21c.zip");
        ArchivePath.UnescapeHost(@"C:\a%2521b%21c.zip").Must().Be(@"C:\a%21b!c.zip");
    }
}
