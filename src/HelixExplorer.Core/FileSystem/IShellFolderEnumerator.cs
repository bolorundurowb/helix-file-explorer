using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IShellFolderEnumerator
{
    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default);

    /// <summary>
    /// Moves a recycle-bin item back to <paramref name="destinationPath"/>, or to its recorded
    /// original path when none is given.
    /// </summary>
    /// <returns>
    /// True when the item is verifiably back in place. Undo branches on this rather than catching an
    /// exception, because a failed restore is an expected outcome (the bin was emptied, the target is
    /// occupied) rather than an error.
    /// </returns>
    ValueTask<bool> RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default);

    ValueTask EmptyRecycleBinAsync(CancellationToken ct = default);

    ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default);

    /// <summary>
    /// Fast filesystem-based check; does not enumerate the shell namespace.
    /// </summary>
    bool HasRecycleBinItems();

    event EventHandler? RecycleBinChanged;

    void StartRecycleBinWatcher();

    void StopRecycleBinWatcher();
}
