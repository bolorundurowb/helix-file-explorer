using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public sealed class FileVisualServiceTests
{
    [Fact]
    public void CachePathFor_GenericFile_KeysByNormalizedExtension()
    {
        FileVisualService.CachePathFor(@"C:\a\notes.txt", isDirectory: false, preferThumbnail: false).Must().Be(".TXT");
        FileVisualService.CachePathFor(@"C:\b\other.txt", isDirectory: false, preferThumbnail: false).Must().Be(".TXT");
        FileVisualService.CachePathFor(@"C:\c\NOTES.TXT", isDirectory: false, preferThumbnail: false).Must().Be(".TXT");
    }

    [Fact]
    public void CachePathFor_Directory_KeysByFullPath()
    {
        FileVisualService.CachePathFor(@"C:\a\folder", isDirectory: true, preferThumbnail: false).Must().Be(@"C:\a\folder");
    }

    [Fact]
    public void CachePathFor_Thumbnail_KeysByFullPath()
    {
        FileVisualService.CachePathFor(@"C:\a\pic.png", isDirectory: false, preferThumbnail: true).Must().Be(@"C:\a\pic.png");
    }

    [Fact]
    public void CachePathFor_PerFileIconExtension_KeysByFullPath()
    {
        FileVisualService.CachePathFor(@"C:\a\app.exe", isDirectory: false, preferThumbnail: false).Must().Be(@"C:\a\app.exe");
        FileVisualService.CachePathFor(@"C:\a\shortcut.lnk", isDirectory: false, preferThumbnail: false).Must().Be(@"C:\a\shortcut.lnk");
    }

    [Fact]
    public void CachePathFor_NoExtension_KeysByEmptyString()
    {
        FileVisualService.CachePathFor(@"C:\a\README", isDirectory: false, preferThumbnail: false).Must().Be(string.Empty);
    }
}
