using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacQuickAccessProvider(ILogger<MacQuickAccessProvider> logger) : IQuickAccessProvider
{
    private static readonly (KnownFolderKind Kind, string Path, string DisplayName)[] PinnedDefaults = [
        (KnownFolderKind.Home, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home"),
        (KnownFolderKind.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Desktop"),
        (KnownFolderKind.Documents, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents"),
        (KnownFolderKind.Downloads, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "Downloads"),
        (KnownFolderKind.Music, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music"),
        (KnownFolderKind.Pictures, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures"),
        (KnownFolderKind.Videos, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Videos"),
        (KnownFolderKind.RecycleBin, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash"), "Trash")
    ];

    public string? GetPath(KnownFolderKind folder)
    {
        try
        {
            return folder switch
            {
                KnownFolderKind.Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                KnownFolderKind.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                KnownFolderKind.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                KnownFolderKind.Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                KnownFolderKind.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                KnownFolderKind.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                KnownFolderKind.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                KnownFolderKind.RecycleBin => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash"),
                _ => null
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resolve known folder '{Folder}'", folder);
            return null;
        }
    }

    public IReadOnlyList<(KnownFolderKind Kind, string Path, string DisplayName)> GetPinnedDefaults()
        => PinnedDefaults.Where(p => Directory.Exists(p.Path) || File.Exists(p.Path)).ToArray();
}