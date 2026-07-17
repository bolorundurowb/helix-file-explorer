using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileVisualRulesTests
{
    [Fact]
    public void SupportsThumbnail_matches_common_image_extensions()
    {
        FileVisualRules.SupportsThumbnail("photo.jpg").Must().BeTrue();
        FileVisualRules.SupportsThumbnail("photo.JPEG").Must().BeTrue();
        FileVisualRules.SupportsThumbnail("doc.pdf").Must().BeFalse();
    }

    [Fact]
    public void PreferThumbnail_only_for_grid_images()
    {
        FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: true).Must().BeTrue();
        FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: false).Must().BeFalse();
        FileVisualRules.PreferThumbnail(@"C:\dir", isDirectory: true, gridView: true).Must().BeFalse();
    }
}
