namespace HelixExplorer.Core.Persistence;

/// <summary>
/// Store for per-folder color overrides, backed by SQLite.
/// Paths are pre-normalized by the caller (trimmed of trailing separators).
/// </summary>
public interface IFolderColorStore
{
    IReadOnlyDictionary<string, uint> LoadAll();

    void Upsert(string normalizedPath, uint argb);

    void Delete(string normalizedPath);
}
