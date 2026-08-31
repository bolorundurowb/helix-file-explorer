namespace HelixExplorer.Core.FileSystem.Undo;

/// <summary>
/// Process-wide undo/redo stack for reversible file operations, like Explorer's.
/// </summary>
/// <remarks>
/// Registered as a singleton: a paste in one window is undoable from another, because the filesystem
/// the operations act on is shared. Not persisted — the stack dies with the process, since recorded
/// paths go stale once anything outside the app touches them.
/// </remarks>
public interface IFileOperationHistory
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>Description of the batch <see cref="TryPopUndo"/> would return, or null.</summary>
    string? UndoDescription { get; }

    /// <summary>Description of the batch <see cref="TryPopRedo"/> would return, or null.</summary>
    string? RedoDescription { get; }

    /// <summary>Raised whenever the stacks change, so command CanExecute can be refreshed.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Records a completed forward operation and clears the redo stack. Batches with no changes are
    /// ignored, so callers do not have to check.
    /// </summary>
    void Push(FileOperationBatch batch);

    bool TryPopUndo(out FileOperationBatch batch);

    bool TryPopRedo(out FileOperationBatch batch);

    /// <summary>
    /// Records the result of a successful inverse on the opposite stack, without clearing anything.
    /// </summary>
    void PushInverse(FileOperationBatch batch, bool wasUndo);

    /// <summary>
    /// Drops everything. Used when the app can no longer trust recorded paths.
    /// </summary>
    void Clear();
}
