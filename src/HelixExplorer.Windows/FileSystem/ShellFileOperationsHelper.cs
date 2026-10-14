using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.Shell;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Shell <see cref="IFileOperation"/> via Vanara — recycle-bin, elevation, progress UI, and shell items.
/// </summary>
internal static class ShellFileOperationsHelper
{
    /// <summary>
    /// Sends <paramref name="paths"/> to the recycle bin.
    /// </summary>
    /// <returns>
    /// Whether the operation ran to completion, any shell HRESULT, and the source paths that were
    /// actually removed. The caller needs the paths, not just a count: one protected item must not
    /// fail siblings the shell already deleted, and undo has to know which items produced a bin entry.
    /// </returns>
    public static Task<ShellDeleteBatch> DeleteToRecycleBinAsync(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => DeleteToRecycleBin(paths, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Permanently deletes items the current token was refused access to, letting the shell raise a
    /// UAC prompt for them as Explorer does.
    /// </summary>
    /// <returns>The paths that no longer exist afterwards, plus the shell HRESULT when the batch aborted.</returns>
    public static Task<ShellDeleteBatch> DeletePermanentlyElevatedAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => DeletePermanentlyElevated(paths, cancellationToken), cancellationToken);
    }

    public static Task<bool> RestoreFromRecycleBinAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => RestoreFromRecycleBin(sourcePath, destinationPath, cancellationToken), cancellationToken);
    }

    public static Task<bool> CanMoveToRecycleBinAsync(string path, CancellationToken cancellationToken)
    {
        return STATask.Run(() => CanMoveToRecycleBin(path, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Sends a single item the caller is about to overwrite to the recycle bin, if the shell will
    /// take it.
    /// </summary>
    /// <returns>True when the item is now in the bin and the caller may proceed with the overwrite.</returns>
    /// <remarks>
    /// Blocking rather than async because the conflict resolution it serves runs synchronously deep
    /// inside a recursive copy walk. That walk is already on a <see cref="Task.Run"/> worker with no
    /// synchronization context, and the shell calls each hop onto their own STA thread, so the wait
    /// cannot deadlock. Failure is not exceptional: plenty of items (network paths, oversized files,
    /// a disabled bin) simply cannot be recycled, and the caller falls back to a hard delete.
    /// </remarks>
    public static bool TryRecycleDisplacedItem(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!CanMoveToRecycleBinAsync(path, cancellationToken).GetAwaiter().GetResult())
                return false;

            var batch = DeleteToRecycleBinAsync([path], progress: null, cancellationToken)
                .GetAwaiter().GetResult();

            // The item is in the bin even when a later sibling aborted the batch.
            return batch.DeletedPaths.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static ShellDeleteBatch DeleteToRecycleBin(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var op = CreateOperation(DeletePromptPolicy.Silent.WithElevation(recycle: true));

        var total = paths.Count;
        var completed = 0;
        var deleted = new List<string>(total);

        op.PostDeleteItem += (s, e) =>
        {
            completed++;
            var parsingName = e.SourceItem?.ParsingName;

            if (e.Result.Succeeded && !string.IsNullOrEmpty(parsingName))
                deleted.Add(parsingName);

            progress?.Report(new FileOperationProgress(completed, total, parsingName, FileOperationKind.Delete));
        };

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var item = new ShellItem(path);
            op.QueueDeleteOperation(item);
        }

        try
        {
            op.PerformOperations();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A declined elevation aborts the batch with an HRESULT whose text is "Catastrophic failure"
            // or a localized equivalent. Keep every path the shell already removed and let the caller
            // describe the rest; never return ex.Message.
            IncludeGone(paths, deleted);
            return new ShellDeleteBatch(Completed: false, ex.HResult, deleted);
        }

        IncludeGone(paths, deleted);
        return new ShellDeleteBatch(!op.AnyOperationsAborted, FailureHResult: null, deleted);
    }

    private static ShellDeleteBatch DeletePermanentlyElevated(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var op = CreateOperation(DeletePromptPolicy.Silent.WithElevation(recycle: false));

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var item = new ShellItem(path);
            op.QueueDeleteOperation(item);
        }

        int? failureHResult = null;
        try
        {
            op.PerformOperations();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Declined or failed UAC. Existence below decides which items were still removed.
            failureHResult = ex.HResult;
        }

        // Existence is the ground truth: elevated deletes run in a broker process, so the per-item
        // callbacks are not a reliable record of what was removed. A protected sibling must not
        // hide items this pass already deleted. Skip the abort query when the shell already threw.
        var completed = failureHResult is null && !op.AnyOperationsAborted;
        return new ShellDeleteBatch(completed, failureHResult, PathsThatAreGone(paths));
    }

    private static bool RestoreFromRecycleBin(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.NoConfirmMkDir;

        var destDir = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(destDir))
            return false;

        using var sourceItem = new ShellItem(sourcePath);
        using var destFolder = new ShellFolder(destDir);
        var destName = Path.GetFileName(destinationPath);

        op.QueueMoveOperation(sourceItem, destFolder, destName);
        op.PerformOperations();

        return !op.AnyOperationsAborted;
    }

    private static bool CanMoveToRecycleBin(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.RecycleOnDelete;

        var canRecycle = true;
        var testComplete = false;

        op.PreDeleteItem += (s, e) =>
        {
            if (!e.Flags.HasFlag(ShellFileOperations.TransferFlags.DeleteRecycleIfPossible))
                canRecycle = false;

            testComplete = true;
            throw new OperationCanceledException("Recycle bin test aborted after pre-flight check.");
        };

        using var item = new ShellItem(path);
        op.QueueDeleteOperation(item);

        try
        {
            op.PerformOperations();
        }
        catch (OperationCanceledException)
        {
            // Abort is success for CanRecycle dry-run; don't treat as failure.
        }
        catch
        {
            return false;
        }

        return testComplete && canRecycle;
    }

    private static ShellFileOperations CreateOperation()
        => CreateOperation(DeletePromptPolicy.Silent);

    private static ShellFileOperations CreateOperation(DeletePromptPolicy policy)
    {
        var op = new ShellFileOperations(HWND.NULL)
        {
            Options = FlagsFor(policy)
        };
        return op;
    }

    /// <summary>
    /// Builds shell flags for <paramref name="policy"/>, starting from the silent baseline that
    /// includes <see cref="ShellFileOperations.OperationFlags.NoErrorUI"/>. Elevation has to clear
    /// that bit; OR-ing <see cref="ShellFileOperations.OperationFlags.ShowElevationPrompt"/> on top
    /// leaves it set and the shell returns <c>E_UNEXPECTED</c> instead of showing UAC.
    /// </summary>
    internal static ShellFileOperations.OperationFlags FlagsFor(DeletePromptPolicy policy)
    {
        var options =
            ShellFileOperations.OperationFlags.Silent |
            ShellFileOperations.OperationFlags.NoConfirmation |
            ShellFileOperations.OperationFlags.NoErrorUI;

        if (policy.RecycleOnDelete)
            options |= ShellFileOperations.OperationFlags.RecycleOnDelete;

        if (policy.ShowElevationPrompt)
            options |= ShellFileOperations.OperationFlags.ShowElevationPrompt;

        if (!policy.SuppressErrorUi)
            options &= ~ShellFileOperations.OperationFlags.NoErrorUI;

        return options;
    }

    private static void IncludeGone(IReadOnlyList<string> paths, List<string> deleted)
    {
        var seen = new HashSet<string>(deleted, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (seen.Contains(path) || PathStillExists(path))
                continue;

            deleted.Add(path);
            seen.Add(path);
        }
    }

    private static List<string> PathsThatAreGone(IReadOnlyList<string> paths)
    {
        var gone = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (!PathStillExists(path))
                gone.Add(path);
        }

        return gone;
    }

    private static bool PathStillExists(string path) => File.Exists(path) || Directory.Exists(path);
}

/// <summary>
/// Outcome of one shell delete batch. <see cref="FailureHResult"/> is the numeric code only;
/// callers map it to user text and must not surface the exception message.
/// </summary>
internal readonly record struct ShellDeleteBatch(
    bool Completed,
    int? FailureHResult,
    IReadOnlyList<string> DeletedPaths);
