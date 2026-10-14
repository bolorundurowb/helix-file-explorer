using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileVisualRulesTests
{
    [Fact]
    public void SupportsThumbnail_MatchesCommonImageExtensions()
    {
        FileVisualRules.SupportsThumbnail("photo.jpg").Must().BeTrue();
        FileVisualRules.SupportsThumbnail("photo.JPEG").Must().BeTrue();
        FileVisualRules.SupportsThumbnail("doc.pdf").Must().BeFalse();
    }

    [Fact]
    public void PreferThumbnail_OnlyTrueForGridImages()
    {
        FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: true).Must().BeTrue();
        FileVisualRules.PreferThumbnail(@"C:\a.png", isDirectory: false, gridView: false).Must().BeFalse();
        FileVisualRules.PreferThumbnail(@"C:\dir", isDirectory: true, gridView: true).Must().BeFalse();
    }

    [Fact]
    public void HasPerFileIcon_TrueOnlyForIconBearingFiles()
    {
        FileVisualRules.HasPerFileIcon(@"C:\app.exe").Must().BeTrue();
        FileVisualRules.HasPerFileIcon(@"C:\lib.DLL").Must().BeTrue();
        FileVisualRules.HasPerFileIcon(@"C:\shortcut.lnk").Must().BeTrue();
        FileVisualRules.HasPerFileIcon(@"C:\icon.ico").Must().BeTrue();
        FileVisualRules.HasPerFileIcon(@"C:\app.EXE").Must().BeTrue();
        FileVisualRules.HasPerFileIcon(@"C:\link.url").Must().BeTrue();

        FileVisualRules.HasPerFileIcon(@"C:\notes.txt").Must().BeFalse();
        FileVisualRules.HasPerFileIcon(@"C:\image.png").Must().BeFalse();
        FileVisualRules.HasPerFileIcon(@"C:\noextension").Must().BeFalse();
    }

    [Fact]
    public void AssociationInvalidation_RemovesExtensionKeys_KeepsFullPathKeys()
    {
        string[] keys =
        [
            ".TXT",
            ".PNG",
            string.Empty,
            @"C:\apps\helix.exe",
            @"C:\links\docs.lnk",
            @"C:\photos\pic.png",
            @"D:\Work\src",
            @"\\server\share\tool.exe"
        ];

        var removed = keys.Where(FileVisualRules.IsExtensionCacheKey).ToArray();
        var kept = keys.Where(key => !FileVisualRules.IsExtensionCacheKey(key)).ToArray();

        removed.Must().BeSequenceEqual([".TXT", ".PNG", string.Empty]);
        kept.Must().BeSequenceEqual(
        [
            @"C:\apps\helix.exe",
            @"C:\links\docs.lnk",
            @"C:\photos\pic.png",
            @"D:\Work\src",
            @"\\server\share\tool.exe"
        ]);
    }
}
