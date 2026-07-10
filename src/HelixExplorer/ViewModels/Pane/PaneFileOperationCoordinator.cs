using System.Diagnostics;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>Clipboard paste, drag-drop, and path helpers for pane file operations.</summary>
public sealed class PaneFileOperationCoordinator
{
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IOsFileClipboard _osClipboard;
    private readonly IUserDialogService _dialogs;
    private readonly IFileOperationReporter _operationReporter;

    public PaneFileOperationCoordinator(
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        IUserDialogService dialogs,
        IFileOperationReporter operationReporter)
    {
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _dialogs = dialogs;
        _operationReporter = operationReporter;
    }

    public async Task PasteAsync(
        string currentPath,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
    {
        if (string.IsNullOrEmpty(currentPath))
            return;

        try
        {
            var payload = await ResolvePastePayloadAsync(currentPath).ConfigureAwait(true);
            if (payload is null || payload.Paths.Count == 0)
            {
                setStatusText("Clipboard has no files");
                return;
            }

            var kind = payload.Operation == ClipboardOperation.Cut
                ? FileOperationKind.Move
                : FileOperationKind.Copy;
            var title = kind == FileOperationKind.Move ? "Moving items…" : "Copying items…";
            _operationReporter.Begin(kind, payload.Paths.Count, title);

            var progress = new Progress<FileOperationProgress>(p => _operationReporter.Report(p));
            var conflicts = FileOperationUiHelper.CreateConflictResolver(_dialogs);
            FileOperationResult result;
            if (payload.Operation == ClipboardOperation.Cut)
            {
                result = await _fileOps.MoveAsync(payload.Paths, currentPath, progress, conflicts).ConfigureAwait(true);
                if (result.Succeeded > 0)
                    _clipboard.Clear();
            }
            else
            {
                result = await _fileOps.CopyAsync(payload.Paths, currentPath, progress, conflicts).ConfigureAwait(true);
            }

            await refreshAsync().ConfigureAwait(true);
            _operationReporter.Complete(
                kind,
                result.Succeeded,
                result.Succeeded > 0
                    ? (kind == FileOperationKind.Move
                        ? $"Moved {result.Succeeded} item(s)"
                        : $"Copied {result.Succeeded} item(s)")
                    : "No items copied");

            await FileOperationUiHelper.ReportResultAsync(
                _dialogs,
                result,
                kind == FileOperationKind.Move ? "Move" : "Copy",
                setStatusText).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Paste failed: {ex.Message}");
            await _dialogs.ShowErrorAsync("Paste failed", ex.Message).ConfigureAwait(true);
            setStatusText("Paste failed");
            _operationReporter.Fail("Paste failed");
        }
    }

    public async Task HandleDropAsync(
        string currentPath,
        IReadOnlyList<string> paths,
        bool isCopy,
        Func<Task> refreshAsync,
        Action<string> setStatusText)
    {
        if (string.IsNullOrEmpty(currentPath) || paths.Count == 0)
            return;

        var filtered = paths
            .Where(p => !IsSameOrChildPath(currentPath, p))
            .ToList();
        if (filtered.Count == 0)
            return;

        try
        {
            var kind = isCopy ? FileOperationKind.Copy : FileOperationKind.Move;
            _operationReporter.Begin(
                kind,
                filtered.Count,
                isCopy ? "Copying items…" : "Moving items…");

            var progress = new Progress<FileOperationProgress>(p => _operationReporter.Report(p));
            var conflicts = FileOperationUiHelper.CreateConflictResolver(_dialogs);
            FileOperationResult result;
            if (isCopy)
                result = await _fileOps.CopyAsync(filtered, currentPath, progress, conflicts).ConfigureAwait(true);
            else
                result = await _fileOps.MoveAsync(filtered, currentPath, progress, conflicts).ConfigureAwait(true);

            await refreshAsync().ConfigureAwait(true);
            _operationReporter.Complete(
                kind,
                result.Succeeded,
                isCopy ? $"Copied {result.Succeeded} item(s)" : $"Moved {result.Succeeded} item(s)");

            await FileOperationUiHelper.ReportResultAsync(
                _dialogs,
                result,
                isCopy ? "Copy" : "Move",
                setStatusText).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Drop failed: {ex.Message}");
            await _dialogs.ShowErrorAsync("Drop failed", ex.Message).ConfigureAwait(true);
            setStatusText("Drop failed");
            _operationReporter.Fail("Drop failed");
        }
    }

    public async Task PublishToOsClipboardAsync(IReadOnlyList<string> paths, ClipboardOperation operation)
    {
        try
        {
            await _osClipboard.SetFilesAsync(paths, operation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OS clipboard publish failed: {ex.Message}");
        }
    }

    public static string? GetPhysicalHostDirectory(string currentPath, bool isArchive)
    {
        if (ArchivePath.IsVirtual(currentPath)
            && ArchivePath.TryParse(currentPath, out var archiveFile, out _))
        {
            return Path.GetDirectoryName(archiveFile);
        }

        if (!isArchive && !string.IsNullOrEmpty(currentPath))
            return currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return null;
    }

    public static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 100; i++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{fileName} ({Guid.NewGuid():N}){extension}");
    }

    public static string GetUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
            return path;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate))
                return candidate;
        }

        return $"{path} ({Guid.NewGuid():N})";
    }

    public static bool IsSameOrChildPath(string directory, string path)
    {
        var dir = directory.TrimEnd('\\', '/');
        var candidate = path.TrimEnd('\\', '/');
        if (string.Equals(dir, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = dir + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ClipboardPayload?> ResolvePastePayloadAsync(string currentPath)
    {
        if (_clipboard.Current is { } internalPayload)
            return internalPayload;

        var os = await _osClipboard.TryGetFilesAsync().ConfigureAwait(true);
        if (os is null)
            return null;

        return new ClipboardPayload(os.Value.Operation, os.Value.Paths, currentPath);
    }
}
