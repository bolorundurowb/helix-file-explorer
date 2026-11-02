namespace HelixExplorer.Core.Persistence;

/// <summary>
/// In-memory lookup for per-folder custom icon file paths.
/// Paths are pre-normalized by the caller.
/// </summary>
public interface IFolderIconService
{
    /// <summary>
    /// Raised after a folder's icon is set or cleared. The event argument is the normalised folder path.
    /// </summary>
    event EventHandler<string>? IconsChanged;

    bool TryGetIconFile(string path, out string iconFilePath);

    void SetIcon(string path, string iconFilePath);

    void RemoveIcon(string path);
}
