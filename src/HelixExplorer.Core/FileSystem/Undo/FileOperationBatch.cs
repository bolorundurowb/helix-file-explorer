using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem.Undo;

/// <summary>
/// What a batch did, which determines the inverse the undo executor dispatches.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="FileOperationKind"/>, which has only Copy/Move/Delete and
/// drives the progress reporter. Undo needs to tell a rename from a move and an extract from a copy
/// because their inverses differ, but widening the reporter's enum would ripple into every UI switch.
/// </remarks>
public enum UndoableOperationKind
{
    Copy,
    Move,
    Rename,
    RecycleDelete,
    CreateFolder,
    Extract,
    Compress
}

/// <summary>
/// One user-visible operation, recorded so it can be applied in reverse.
/// </summary>
/// <param name="Kind">Selects the inverse.</param>
/// <param name="Description">
/// User-facing summary shown next to Undo/Redo, e.g. "copy of 3 items".
/// </param>
/// <param name="Changes">
/// Top-level changes that actually succeeded. A partially successful batch carries only its successes,
/// so undo never touches items the forward operation failed to handle.
/// </param>
public sealed record FileOperationBatch(
    UndoableOperationKind Kind,
    string Description,
    IReadOnlyList<FileOperationChange> Changes)
{
    /// <summary>
    /// Sources the operation started from, needed to redo a compress (the zip has to be rebuilt from
    /// the originals, which are not derivable from the zip path alone).
    /// </summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// True when the destination the batch replaced was recycled rather than permanently deleted, so
    /// undo may honestly claim the displaced data is recoverable from the bin.
    /// </summary>
    public bool ReplacedItemsRecycled { get; init; }
}
