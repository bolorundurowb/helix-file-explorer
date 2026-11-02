namespace HelixExplorer.Core.Persistence;

/// <summary>
/// Store for per-folder custom icon file paths, backed by SQLite.
/// Paths are pre-normalized by the caller (trimmed of trailing separators).
/// The stored path points at a file Helix owns; it is not the original user selection.
/// </summary>
public interface IFolderIconStore
{
    IReadOnlyDictionary<string, string> LoadAll();

    void Upsert(string normalizedPath, string iconFilePath);

    void Delete(string normalizedPath);
}
