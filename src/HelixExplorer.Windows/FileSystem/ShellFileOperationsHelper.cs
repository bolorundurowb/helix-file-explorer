using System.Runtime.InteropServices;
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
    /// Whether the operation ran to completion, and the source paths the shell confirmed it deleted.
    /// The caller needs the actual paths, not just a count: the shell may fail any item in the batch,
    /// and undo has to know which items really produced a bin entry.
    /// </returns>
    public static Task<(bool Success, IReadOnlyList<string> DeletedPaths)> DeleteToRecycleBinAsync(
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
    /// <returns>The paths that no longer exist afterwards.</returns>
    public static Task<IReadOnlyList<string>> DeletePermanentlyElevatedAsync(
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

            var (success, deleted) = DeleteToRecycleBinAsync([path], progress: null, cancellationToken)
                .GetAwaiter().GetResult();

            return success && deleted.Count > 0;
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

    private static (bool Success, IReadOnlyList<string> DeletedPaths) DeleteToRecycleBin(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var op = CreateOperation();
        // NoErrorUI otherwise suppresses the UAC prompt too, and the shell then fails the whole batch
        // with E_UNEXPECTED ("Catastrophic failure") as soon as one item needs admin rights.
        op.Options |= ShellFileOperations.OperationFlags.RecycleOnDelete
                      | ShellFileOperations.OperationFlags.ShowElevationPrompt;

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
        catch (COMException)
        {
            // A declined elevation or an item the shell cannot touch aborts the batch with a failing
            // HRESULT. The items it did process are already in `deleted`; the caller reports the rest
            // per item instead of surfacing the raw HRESULT text.
            return (false, deleted);
        }

        return (!op.AnyOperationsAborted, deleted);
    }

    private static IReadOnlyList<string> DeletePermanentlyElevated(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.ShowElevationPrompt;

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
        catch (COMException)
        {
            // Declined UAC prompt or an item that stays locked; the existence check below decides.
        }

        // Existence is the ground truth: elevated deletes run in a broker process, so the per-item
        // callbacks are not a reliable record of what was removed.
        return paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
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
    {
        var op = new ShellFileOperations(HWND.NULL)
        {
            Options =
                ShellFileOperations.OperationFlags.Silent |
                ShellFileOperations.OperationFlags.NoConfirmation |
                ShellFileOperations.OperationFlags.NoErrorUI
        };
        return op;
    }
}
