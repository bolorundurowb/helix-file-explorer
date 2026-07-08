using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinQuickAccessProvider : IQuickAccessProvider
{
    public string? GetPath(KnownFolderKind folder)
    {
        try
        {
            return folder switch
            {
                KnownFolderKind.Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                KnownFolderKind.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                KnownFolderKind.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                KnownFolderKind.Downloads => GetDownloadsFolder(),
                KnownFolderKind.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                KnownFolderKind.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                KnownFolderKind.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                KnownFolderKind.RecycleBin => "shell:RecycleBinFolder",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<(KnownFolderKind Kind, string Path, string DisplayName)> GetPinnedDefaults()
    {
        var result = new List<(KnownFolderKind, string, string)>(6);
        Add(result, KnownFolderKind.Desktop, "Desktop");
        Add(result, KnownFolderKind.Downloads, "Downloads");
        Add(result, KnownFolderKind.Documents, "Documents");
        Add(result, KnownFolderKind.Music, "Music");
        Add(result, KnownFolderKind.Pictures, "Pictures");
        Add(result, KnownFolderKind.Videos, "Videos");
        return result;
    }

    private void Add(List<(KnownFolderKind, string, string)> list, KnownFolderKind kind, string displayName)
    {
        var path = GetPath(kind);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            list.Add((kind, EnsureTrailingSeparator(path), displayName));
    }

    private static string GetDownloadsFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(profile, "Downloads");
        return Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
