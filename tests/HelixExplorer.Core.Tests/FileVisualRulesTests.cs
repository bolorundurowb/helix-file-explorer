using HelixExplorer.Core.FileSystem;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class FileVisualRulesTests
{
    [Fact]
    public void SupportsThumbnail_matches_common_image_extensions()
    {
        Assert.True(FileVisualRules.SupportsThumbnail("photo.jpg"));
        Assert.True(FileVisualRules.SupportsThumbnail("photo.JPEG"));
        Assert.False(FileVisualRules.SupportsThumbnail("doc.pdf"));
    }

    [Fact]
    public void PreferThumbnail_only_for_grid_images()
    {
        Assert.True(FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: true));
        Assert.False(FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: false));
        Assert.False(FileVisualRules.PreferThumbnail(@"C:\dir", isDirectory: true, gridView: true));
    }
}
