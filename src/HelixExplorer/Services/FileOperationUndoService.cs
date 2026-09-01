using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Localization;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Services;

/// <summary>
/// Applies the inverse of a recorded file operation.
/// </summary>
/// <remarks>
/// Window-scoped even though <see cref="IFileOperationHistory"/> is a singleton: progress belongs to
/// the window the user pressed the shortcut in, and reaching for another window's reporter would put
/// a progress bar somewhere they are not looking. The history's own lock keeps two windows from
/// popping the same batch.
/// <para>
/// Inverses run through the same <see cref="IFileOperationService"/> as forward operations, with no
/// conflict UI: a destination that is unexpectedly occupied means the recording is stale, and the
/// right answer is to fail and forget the entry rather than ask the user to arbitrate.
/// </para>
/// </remarks>
public sealed class FileOperationUndoService(
    IFileOperationHistory history,
    IFileOperationService fileOps,
    IRecycleBinService recycleBin,
    IArchiveProvider archive,
    IFileOperationReporter reporter,
    IUserDialogService dialogs,
    ILogger<FileOperationUndoService> logger)
{
    public bool CanUndo => history.CanUndo && !reporter.IsBusy;

    public bool CanRedo => history.CanRedo && !reporter.IsBusy;

    public string? UndoDescription => history.UndoDescription;

    public string? RedoDescription => history.RedoDescription;

    public event EventHandler? Changed
    {
        add => history.Changed += value;
        remove => history.Changed -= value;
    }

    public Task<string> UndoAsync(CancellationToken ct = default) => ApplyAsync(isUndo: true, ct);

    public Task<string> RedoAsync(CancellationToken ct = default) => ApplyAsync(isUndo: false, ct);

    /// <returns>Status text describing what happened, for the caller to surface.</returns>
    private async Task<string> ApplyAsync(bool isUndo, CancellationToken ct)
    {
        // Refusing while this window is mid-operation rather than queueing: an inverse computed against
        // paths a running batch is still rewriting would act on the wrong state.
        if (reporter.IsBusy)
            return UiStrings.UndoBusy;

        FileOperationBatch batch;
        var popped = isUndo ? history.TryPopUndo(out batch) : history.TryPopRedo(out batch);

        if (!popped)
            return isUndo ? UiStrings.NothingToUndo : UiStrings.NothingToRedo;

        var kind = ReporterKindFor(batch, isUndo);
        reporter.Begin(kind, batch.Changes.Count, isUndo ? UiStrings.Undoing : UiStrings.Redoing);

        try
        {
            var inverse = await ApplyBatchAsync(batch, isUndo, ct).ConfigureAwait(true);
            if (inverse is null)
            {
                // The entry is dropped, not returned to the stack. Explorer does the same: an inverse
                // that no longer matches the disk will not start matching it later, and leaving it in
                // place would let the user retry a failure indefinitely.
                reporter.Fail(UiStrings.UndoStale);
                await dialogs.ShowErrorAsync(
                    isUndo ? UiStrings.UndoFailed : UiStrings.RedoFailed,
                    UiStrings.UndoStaleDetail).ConfigureAwait(true);

                return UiStrings.UndoStale;
            }

            history.PushInverse(inverse, isUndo);

            var message = isUndo
                ? UiStrings.UndidOperation(batch.Description)
                : UiStrings.RedidOperation(batch.Description);

            reporter.Complete(kind, batch.Changes.Count, message);
            return message;
        }
        catch (OperationCanceledException)
        {
            reporter.Cancelled(UiStrings.OperationCancelled);
            return UiStrings.OperationCancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Direction} failed for '{Description}'", isUndo ? "Undo" : "Redo", batch.Description);
            reporter.Fail(isUndo ? UiStrings.UndoFailed : UiStrings.RedoFailed);
            await dialogs.ShowErrorAsync(
                isUndo ? UiStrings.UndoFailed : UiStrings.RedoFailed,
                ex.Message).ConfigureAwait(true);

            return isUndo ? UiStrings.UndoFailed : UiStrings.RedoFailed;
        }
    }

    /// <returns>
    /// The batch to place on the opposite stack, or null when the inverse could not be applied.
    /// </returns>
    private async Task<FileOperationBatch?> ApplyBatchAsync(FileOperationBatch batch, bool isUndo, CancellationToken ct)
    {
        return batch.Kind switch
        {
            // Copy, extract and compress all created something from nothing, so undo removes the
            // created items and redo re-creates them.
            UndoableOperationKind.Copy => isUndo
                ? await RecycleCreatedAsync(batch, ct).ConfigureAwait(true)
                : await RecopyAsync(batch, ct).ConfigureAwait(true),

            UndoableOperationKind.Move => await MoveBackAsync(batch, isUndo, ct).ConfigureAwait(true),

            UndoableOperationKind.Rename => await RenameBackAsync(batch, isUndo, ct).ConfigureAwait(true),

            UndoableOperationKind.RecycleDelete => isUndo
                ? await RestoreAsync(batch, ct).ConfigureAwait(true)
                : await RecycleOriginalsAsync(batch, ct).ConfigureAwait(true),

            UndoableOperationKind.CreateFolder => isUndo
                ? await RecycleCreatedAsync(batch, ct).ConfigureAwait(true)
                : await RecreateFoldersAsync(batch, ct).ConfigureAwait(true),

            UndoableOperationKind.Extract => isUndo
                ? await RecycleCreatedAsync(batch, ct).ConfigureAwait(true)
                : await ReExtractAsync(batch, ct).ConfigureAwait(true),

            UndoableOperationKind.Compress => isUndo
                ? await RecycleCreatedAsync(batch, ct).ConfigureAwait(true)
                : await RecompressAsync(batch, ct).ConfigureAwait(true),

            _ => null
        };
    }

    /// <summary>
    /// Sends the items an operation created to the recycle bin.
    /// </summary>
    /// <remarks>
    /// Always recycled, never permanently deleted, so a mistaken undo is itself recoverable — the one
    /// case where undo removes data the user may not have meant to lose.
    /// </remarks>
    private async Task<FileOperationBatch?> RecycleCreatedAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var targets = PathUtilities.OrderDeepestFirst(
            [.. batch.Changes.Select(c => c.DestinationPath)]);

        // All recorded dests must still be there. Recycling a subset would leave a redo that tries
        // to recreate items that were never removed.
        if (targets.Count == 0 || targets.Any(p => !Exists(p)))
            return null;

        var result = await fileOps.DeleteAsync(targets, permanently: false, progress: null, ct, reporter)
            .ConfigureAwait(true);

        if (result.Succeeded != targets.Count)
            return null;

        // The inverse of "remove what was created" is "create it again", which is the original batch —
        // the recycle mapping is not needed, since redo re-runs the forward operation.
        return batch;
    }

    private async Task<FileOperationBatch?> RecopyAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var changes = new List<FileOperationChange>(batch.Changes.Count);

        foreach (var change in batch.Changes)
        {
            ct.ThrowIfCancellationRequested();

            if (!Exists(change.SourcePath) || Exists(change.DestinationPath))
                return null;

            var destDir = Path.GetDirectoryName(change.DestinationPath);
            if (string.IsNullOrEmpty(destDir))
                return null;

            var result = await fileOps.CopyAsync(
                [change.SourcePath], destDir, progress: null, conflicts: null, ct, reporter).ConfigureAwait(true);

            if (result.Succeeded == 0)
                return null;

            changes.AddRange(result.Changes);
        }

        return changes.Count == 0 ? null : batch with { Changes = changes };
    }

    private async Task<FileOperationBatch?> MoveBackAsync(FileOperationBatch batch, bool isUndo, CancellationToken ct)
    {
        // Undo walks the batch backwards so the last move is reversed first, which matters when two
        // items in one batch passed through the same intermediate name.
        var changes = isUndo ? batch.Changes.Reverse().ToList() : [.. batch.Changes];
        var applied = new List<FileOperationChange>(changes.Count);

        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();

            var from = isUndo ? change.DestinationPath : change.SourcePath;
            var to = isUndo ? change.SourcePath : change.DestinationPath;

            if (!Exists(from) || Exists(to))
                return null;

            var toDir = Path.GetDirectoryName(to);
            if (string.IsNullOrEmpty(toDir))
                return null;

            // Moving back may need the original parent recreated, if undoing a move out of a folder
            // that was itself removed afterwards.
            Directory.CreateDirectory(toDir);

            var result = await fileOps.MoveAsync(
                [from], toDir, progress: null, conflicts: null, ct, reporter).ConfigureAwait(true);

            if (result.Succeeded == 0)
                return null;

            applied.Add(new FileOperationChange(to, from));
        }

        return applied.Count == 0 ? null : batch with { Changes = applied };
    }

    private async Task<FileOperationBatch?> RenameBackAsync(FileOperationBatch batch, bool isUndo, CancellationToken ct)
    {
        var applied = new List<FileOperationChange>(batch.Changes.Count);

        foreach (var change in batch.Changes)
        {
            ct.ThrowIfCancellationRequested();

            var from = isUndo ? change.DestinationPath : change.SourcePath;
            var to = isUndo ? change.SourcePath : change.DestinationPath;

            if (!Exists(from) || Exists(to))
                return null;

            var newName = Path.GetFileName(to);
            if (string.IsNullOrEmpty(newName))
                return null;

            var result = await fileOps.RenameAsync(from, newName, ct).ConfigureAwait(true);
            if (result.Failed > 0)
                return null;

            applied.Add(new FileOperationChange(to, from));
        }

        return applied.Count == 0 ? null : batch with { Changes = applied };
    }

    private async Task<FileOperationBatch?> RestoreAsync(FileOperationBatch batch, CancellationToken ct)
    {
        if (batch.Changes.Count == 0 || batch.Changes.Any(c => c.RecycleItemPath is null))
            return null;

        // Shallowest first: a restored child needs its parent directory to exist again. The helper
        // is a stable sort, so siblings keep the recorded order when they share a depth.
        var orderedDests = PathUtilities.OrderShallowestFirst(
            [.. batch.Changes.Select(c => c.DestinationPath)]);
        var ordered = orderedDests
            .Select(dest => batch.Changes.First(c => PathUtilities.PathsEqual(c.DestinationPath, dest)))
            .ToList();

        var restored = new List<FileOperationChange>(ordered.Count);

        foreach (var change in ordered)
        {
            ct.ThrowIfCancellationRequested();

            if (Exists(change.DestinationPath))
                return null;

            if (!await recycleBin.RestoreAsync(change.RecycleItemPath!, change.DestinationPath, ct)
                    .ConfigureAwait(true))
            {
                return null;
            }

            restored.Add(change);
        }

        if (restored.Count == 0)
            return null;

        // Redo will recycle these paths again and record fresh $R mappings, so the stale bin paths are
        // deliberately not carried forward.
        return batch with
        {
            Changes = [.. restored.Select(c => new FileOperationChange(c.SourcePath, c.DestinationPath))]
        };
    }

    private async Task<FileOperationBatch?> RecycleOriginalsAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var targets = PathUtilities.OrderDeepestFirst(
            [.. batch.Changes.Select(c => c.DestinationPath)]);

        if (targets.Count == 0 || targets.Any(p => !Exists(p)))
            return null;

        var result = await fileOps.DeleteAsync(targets, permanently: false, progress: null, ct, reporter)
            .ConfigureAwait(true);

        var restorable = result.Changes.Where(c => c.RecycleItemPath is not null).ToList();
        return restorable.Count == targets.Count ? batch with { Changes = restorable } : null;
    }

    private async Task<FileOperationBatch?> RecreateFoldersAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var applied = new List<FileOperationChange>(batch.Changes.Count);

        foreach (var change in batch.Changes)
        {
            ct.ThrowIfCancellationRequested();

            if (Exists(change.DestinationPath))
                continue;

            // Directory.CreateDirectory rather than the service's CreateFolderAsync, because that
            // uniquifies and would invent a third name instead of restoring the recorded one.
            await Task.Run(() => Directory.CreateDirectory(change.DestinationPath), ct).ConfigureAwait(true);
            applied.Add(change);
        }

        return applied.Count == 0 ? null : batch with { Changes = applied };
    }

    private async Task<FileOperationBatch?> ReExtractAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var applied = new List<FileOperationChange>(batch.Changes.Count);

        foreach (var change in batch.Changes)
        {
            ct.ThrowIfCancellationRequested();

            if (Exists(change.DestinationPath) || !Exists(change.SourcePath))
                continue;

            if (!archive.IsArchiveFile(change.SourcePath))
                continue;

            await archive.ExtractArchiveToDirectoryAsync(change.SourcePath, change.DestinationPath, ct)
                .ConfigureAwait(true);

            applied.Add(change);
        }

        return applied.Count == 0 ? null : batch with { Changes = applied };
    }

    private async Task<FileOperationBatch?> RecompressAsync(FileOperationBatch batch, CancellationToken ct)
    {
        var change = batch.Changes.FirstOrDefault();
        if (change is null || batch.Sources.Count == 0)
            return null;

        if (Exists(change.DestinationPath) || !batch.Sources.All(Exists))
            return null;

        await archive.CreateZipAsync(batch.Sources, change.DestinationPath, ct).ConfigureAwait(true);
        return batch;
    }

    private static bool Exists(string path)
        => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

    /// <summary>
    /// Which progress icon the inverse should show, since the reporter only knows copy/move/delete.
    /// </summary>
    private static FileOperationKind ReporterKindFor(FileOperationBatch batch, bool isUndo) => batch.Kind switch
    {
        UndoableOperationKind.Move => FileOperationKind.Move,
        UndoableOperationKind.Rename => FileOperationKind.Move,
        UndoableOperationKind.RecycleDelete => isUndo ? FileOperationKind.Copy : FileOperationKind.Delete,
        _ => isUndo ? FileOperationKind.Delete : FileOperationKind.Copy
    };
}
