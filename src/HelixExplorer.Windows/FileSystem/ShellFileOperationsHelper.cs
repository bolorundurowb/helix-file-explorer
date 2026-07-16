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
    public static Task<(bool Success, int SucceededCount)> DeleteToRecycleBinAsync(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return STATask.Run(() => DeleteToRecycleBin(paths, progress, cancellationToken), cancellationToken);
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

    private static (bool Success, int SucceededCount) DeleteToRecycleBin(
        IReadOnlyList<string> paths,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var op = CreateOperation();
        op.Options |= ShellFileOperations.OperationFlags.RecycleOnDelete;

        var total = paths.Count;
        var completed = 0;
        var succeeded = 0;

        op.PostDeleteItem += (s, e) =>
        {
            completed++;
            if (e.Result.Succeeded)
                succeeded++;

            progress?.Report(new FileOperationProgress(completed, total, e.SourceItem?.ParsingName, FileOperationKind.Delete));
        };

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var item = new ShellItem(path);
            op.QueueDeleteOperation(item);
        }

        op.PerformOperations();
        return (!op.AnyOperationsAborted, succeeded);
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
