using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IQuickAccessProvider
{
    string? GetPath(KnownFolderKind folder);

    IReadOnlyList<(KnownFolderKind Kind, string Path, string DisplayName)> GetPinnedDefaults();
}
