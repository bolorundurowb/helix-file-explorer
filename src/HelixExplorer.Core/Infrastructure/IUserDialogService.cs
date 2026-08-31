namespace HelixExplorer.Core.Infrastructure;

public enum FileConflictChoice
{
    Replace,
    KeepBoth,
    Skip,
    Cancel,

    /// <summary>
    /// Directory-only: recursively merge the source into the existing destination, keeping files that
    /// exist only on one side and resolving per-file conflicts. For a move, the source is removed after
    /// the merge. Meaningless for files, where it is treated as <see cref="Replace"/>.
    /// </summary>
    Merge
}

public sealed record FileConflictInfo(string SourcePath, string DestinationPath, bool IsDirectory);

public sealed record FileConflictResolution(FileConflictChoice Choice, bool ApplyToAll);

public sealed record FileOperationFailure(string Path, string Message);

/// <summary>
/// One reversible top-level change produced by a file operation.
/// </summary>
/// <remarks>
/// Deliberately top-level only: copying or moving a directory records the directory itself, never the
/// tree beneath it, so undoing a large paste recycles one folder instead of enumerating thousands of
/// files. See <c>PaneFileOperationCoordinator</c> push sites.
/// </remarks>
/// <param name="SourcePath">Where the item came from. For a rename this is the old path.</param>
/// <param name="DestinationPath">
/// Where the item ended up, after conflict resolution — so this already accounts for Keep Both
/// uniquifying (<c>Foo</c> becoming <c>Foo (2)</c>). For a recycle delete this is the restore target.
/// </param>
/// <param name="RecycleItemPath">
/// The <c>$R*</c> path inside the recycle bin, for recycle deletes only. Null when the shell reported
/// the delete but no matching bin entry could be found, in which case the change is not undoable.
/// </param>
public sealed record FileOperationChange(
    string SourcePath,
    string DestinationPath,
    string? RecycleItemPath = null);

public sealed record FileOperationResult(
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<FileOperationFailure> Failures)
{
    public static FileOperationResult Empty { get; } = new(0, 0, 0, Array.Empty<FileOperationFailure>());

    /// <summary>
    /// Top-level reversible changes, in the order they succeeded. Empty when the operation recorded
    /// nothing invertible.
    /// </summary>
    public IReadOnlyList<FileOperationChange> Changes { get; init; } = [];

    /// <summary>
    /// True when any conflict in the batch was resolved by merging. Merge is not invertible — the
    /// pre-merge state of the destination is unrecoverable — so such batches are never pushed.
    /// </summary>
    public bool UsedMerge { get; init; }

    /// <summary>
    /// True when any conflict was resolved by replacing. Undo can remove what was copied in but can
    /// only bring the displaced item back if it was recycled rather than deleted outright.
    /// </summary>
    public bool UsedReplace { get; init; }

    /// <summary>
    /// False when a replace permanently erased the displaced item because the recycle bin refused it.
    /// </summary>
    public bool ReplacedItemsRecycled { get; init; } = true;

    public bool HasIssues => Failed > 0 || Skipped > 0;

    public static FileOperationResult operator +(FileOperationResult a, FileOperationResult b)
    {
        var failures = new List<FileOperationFailure>(a.Failures.Count + b.Failures.Count);
        failures.AddRange(a.Failures);
        failures.AddRange(b.Failures);

        var changes = new List<FileOperationChange>(a.Changes.Count + b.Changes.Count);
        changes.AddRange(a.Changes);
        changes.AddRange(b.Changes);

        return new FileOperationResult(
            a.Succeeded + b.Succeeded,
            a.Skipped + b.Skipped,
            a.Failed + b.Failed,
            failures)
        {
            Changes = changes,
            UsedMerge = a.UsedMerge || b.UsedMerge,
            UsedReplace = a.UsedReplace || b.UsedReplace,
            ReplacedItemsRecycled = a.ReplacedItemsRecycled && b.ReplacedItemsRecycled
        };
    }
}

/// <summary>
/// Kept separate from <see cref="IUiHost"/> which remains HWND/shell specific.
/// </summary>
public interface IUserDialogService
{
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);

    Task ShowOperationSummaryAsync(FileOperationResult result, string operationName);

    Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll);
}
