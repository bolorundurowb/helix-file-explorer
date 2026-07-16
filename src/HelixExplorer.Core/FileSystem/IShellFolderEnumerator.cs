using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IShellFolderEnumerator
{
    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default);

    /// <summary>
    /// Restores a Recycle Bin item to its original location. The <paramref name="destinationPath"/>
    /// can be omitted when the implementation can derive it from the shell item metadata.
    /// </summary>
    ValueTask RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default);

    ValueTask EmptyRecycleBinAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the total number of items and total size (in bytes) in the Recycle Bin.
    /// </summary>
    ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns true if the Recycle Bin contains any items for the current user. This is a fast
    /// filesystem-based check and does not enumerate the shell namespace.
    /// </summary>
    bool HasRecycleBinItems();

    /// <summary>
    /// Raised when the contents of the Recycle Bin change on disk.
    /// </summary>
    event EventHandler? RecycleBinChanged;

    /// <summary>
    /// Starts watching the Recycle Bin for changes.
    /// </summary>
    void StartRecycleBinWatcher();

    /// <summary>
    /// Stops watching the Recycle Bin for changes.
    /// </summary>
    void StopRecycleBinWatcher();
}
