using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

public class FileOperationUndoServiceTests
{
    [Fact]
    public async Task UndoCopy_RecyclesCreatedDestinations()
    {
        var root = CreateTempDirectory();
        try
        {
            var created = Path.Combine(root, "copied.txt");
            await File.WriteAllTextAsync(created, "x");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Copy,
                "copy of 1 item",
                [new FileOperationChange(Path.Combine(root, "original.txt"), created)]));

            var service = CreateService(history, fileOps);
            await service.UndoAsync();

            // Undo of a copy must recycle, never permanently delete: a mistaken Ctrl+Z has to stay
            // recoverable from the bin.
            fileOps.Deletes.Count.Must().Be(1);
            fileOps.Deletes[0].Permanently.Must().BeFalse();
            fileOps.Deletes[0].Paths.Must().Contain(created);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoCopy_RecyclesOnlyRecordedTopLevelPaths()
    {
        var root = CreateTempDirectory();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            await File.WriteAllTextAsync(Path.Combine(folder, "inside.txt"), "x");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Copy,
                "copy of 1 item",
                [new FileOperationChange(Path.Combine(root, "src"), folder)]));

            await CreateService(history, fileOps).UndoAsync();

            fileOps.Deletes[0].Paths.Count.Must().Be(1);
            fileOps.Deletes[0].Paths[0].Must().Be(folder);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoMove_MovesDestinationBackToOriginalParent()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var moved = Path.Combine(destDir, "file.txt");
            await File.WriteAllTextAsync(moved, "x");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Move,
                "move of 1 item",
                [new FileOperationChange(Path.Combine(sourceDir, "file.txt"), moved)]));

            await CreateService(history, fileOps).UndoAsync();

            fileOps.Moves.Count.Must().Be(1);
            fileOps.Moves[0].Sources[0].Must().Be(moved);
            fileOps.Moves[0].Destination.Must().Be(sourceDir);
            fileOps.Deletes.Count.Must().Be(0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoMove_ReversesOrderWithinTheBatch()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var first = Path.Combine(destDir, "first.txt");
            var second = Path.Combine(destDir, "second.txt");
            await File.WriteAllTextAsync(first, "1");
            await File.WriteAllTextAsync(second, "2");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Move,
                "move of 2 items",
                [
                    new FileOperationChange(Path.Combine(sourceDir, "first.txt"), first),
                    new FileOperationChange(Path.Combine(sourceDir, "second.txt"), second)
                ]));

            await CreateService(history, fileOps).UndoAsync();

            // Last moved is first reversed, so two items that passed through the same intermediate
            // name unwind in the order that keeps each step valid.
            fileOps.Moves[0].Sources[0].Must().Be(second);
            fileOps.Moves[1].Sources[0].Must().Be(first);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoRename_RenamesBackToTheOldName()
    {
        var root = CreateTempDirectory();
        try
        {
            var renamed = Path.Combine(root, "after.txt");
            await File.WriteAllTextAsync(renamed, "x");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Rename,
                "rename to after.txt",
                [new FileOperationChange(Path.Combine(root, "before.txt"), renamed)]));

            await CreateService(history, fileOps).UndoAsync();

            fileOps.Renames.Count.Must().Be(1);
            fileOps.Renames[0].Path.Must().Be(renamed);
            fileOps.Renames[0].NewName.Must().Be("before.txt");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoDelete_RestoresFromTheRecordedBinEntry()
    {
        var root = CreateTempDirectory();
        var shell = new RecordingShellEnumerator();
        var history = new FileOperationHistory();

        var original = Path.Combine(root, "gone.txt");
        history.Push(new FileOperationBatch(
            UndoableOperationKind.RecycleDelete,
            "delete of 1 item",
            [new FileOperationChange(original, original, @"C:\$RECYCLE.BIN\S-1-5-21\$RABC")]));

        try
        {
            await CreateService(history, new RecordingFileOps(), shell).UndoAsync();

            shell.Restores.Count.Must().Be(1);
            shell.Restores[0].ItemPath.Must().Be(@"C:\$RECYCLE.BIN\S-1-5-21\$RABC");
            shell.Restores[0].Destination.Must().Be(original);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UndoCopy_WhenOneOfSeveralDestinationsIsMissing_DoesNotRecycleTheRest()
    {
        var root = CreateTempDirectory();
        try
        {
            var first = Path.Combine(root, "a.txt");
            var second = Path.Combine(root, "b.txt");
            var missing = Path.Combine(root, "gone.txt");
            await File.WriteAllTextAsync(first, "a");
            await File.WriteAllTextAsync(second, "b");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Copy,
                "copy of 3 items",
                [
                    new FileOperationChange(Path.Combine(root, "src-a.txt"), first),
                    new FileOperationChange(Path.Combine(root, "src-b.txt"), second),
                    new FileOperationChange(Path.Combine(root, "src-gone.txt"), missing)
                ]));

            await CreateService(history, fileOps).UndoAsync();

            // A partial inverse would leave two files recycled and one still on disk, with a redo
            // that tries to recreate all three. Fail the whole batch instead.
            fileOps.Deletes.Count.Must().Be(0);
            history.CanUndo.Must().BeFalse();
            history.CanRedo.Must().BeFalse();
            File.Exists(first).Must().BeTrue();
            File.Exists(second).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Undo_WhenTargetIsMissing_DropsTheEntryInsteadOfRetrying()
    {
        var history = new FileOperationHistory();
        history.Push(new FileOperationBatch(
            UndoableOperationKind.Copy,
            "copy of 1 item",
            [new FileOperationChange(@"C:\nowhere\src.txt", @"C:\nowhere\dest.txt")]));

        var fileOps = new RecordingFileOps();
        await CreateService(history, fileOps).UndoAsync();

        // Nothing was attempted, and the stale entry is gone rather than sitting there inviting the
        // user to fail again.
        fileOps.Deletes.Count.Must().Be(0);
        history.CanUndo.Must().BeFalse();
        history.CanRedo.Must().BeFalse();
    }

    [Fact]
    public async Task Undo_WhileAnOperationIsRunning_DoesNothing()
    {
        var history = new FileOperationHistory();
        history.Push(new FileOperationBatch(
            UndoableOperationKind.Copy,
            "copy of 1 item",
            [new FileOperationChange(@"C:\a", @"C:\b")]));

        var fileOps = new RecordingFileOps();
        var service = CreateService(history, fileOps, reporter: new StubReporter { IsBusy = true });

        await service.UndoAsync();

        fileOps.Deletes.Count.Must().Be(0);

        // The batch stays put: it was never popped, so the user can undo once the operation finishes.
        history.CanUndo.Must().BeTrue();
    }

    [Fact]
    public async Task Undo_ThenRedo_ReappliesTheOriginalOperation()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var source = Path.Combine(sourceDir, "file.txt");
            var dest = Path.Combine(destDir, "file.txt");
            await File.WriteAllTextAsync(source, "x");
            await File.WriteAllTextAsync(dest, "x");

            var fileOps = new RecordingFileOps();
            var history = new FileOperationHistory();
            history.Push(new FileOperationBatch(
                UndoableOperationKind.Copy,
                "copy of 1 item",
                [new FileOperationChange(source, dest)]));

            var service = CreateService(history, fileOps);
            await service.UndoAsync();

            history.CanRedo.Must().BeTrue();

            // The fake does not touch the disk, so remove the copy to model the state after undo.
            File.Delete(dest);
            await service.RedoAsync();

            fileOps.Copies.Count.Must().Be(1);
            fileOps.Copies[0].Sources[0].Must().Be(source);
            fileOps.Copies[0].Destination.Must().Be(destDir);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static FileOperationUndoService CreateService(
        IFileOperationHistory history,
        IFileOperationService fileOps,
        IShellFolderEnumerator? shell = null,
        IFileOperationReporter? reporter = null)
        => new(
            history,
            fileOps,
            shell ?? new RecordingShellEnumerator(),
            new UnusedArchiveProvider(),
            reporter ?? new StubReporter(),
            new SilentDialogs(),
            NullLogger<FileOperationUndoService>.Instance);

    private static string CreateTempDirectory()
        => Directory.CreateTempSubdirectory("helix-undo-tests-").FullName;

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for CI temp files.
        }
    }

    /// <summary>
    /// Records what the undo service asked for without touching the filesystem, so the tests assert
    /// on dispatch rather than on shell behaviour.
    /// </summary>
    private sealed class RecordingFileOps : IFileOperationService
    {
        public List<(IReadOnlyList<string> Sources, string Destination)> Copies { get; } = [];
        public List<(IReadOnlyList<string> Sources, string Destination)> Moves { get; } = [];
        public List<(IReadOnlyList<string> Paths, bool Permanently)> Deletes { get; } = [];
        public List<(string Path, string NewName)> Renames { get; } = [];

        public ValueTask<FileOperationResult> CopyAsync(IReadOnlyList<string> sources, string destination, IProgress<FileOperationProgress>? progress = null, IFileConflictResolver? conflicts = null, CancellationToken ct = default, IFileOperationControl? control = null)
        {
            Copies.Add((sources, destination));
            return ValueTask.FromResult(Success(sources, destination));
        }

        public ValueTask<FileOperationResult> MoveAsync(IReadOnlyList<string> sources, string destination, IProgress<FileOperationProgress>? progress = null, IFileConflictResolver? conflicts = null, CancellationToken ct = default, IFileOperationControl? control = null)
        {
            Moves.Add((sources, destination));
            return ValueTask.FromResult(Success(sources, destination));
        }

        public ValueTask<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanently, IProgress<FileOperationProgress>? progress = null, CancellationToken ct = default, IFileOperationControl? control = null)
        {
            Deletes.Add((paths, permanently));
            return ValueTask.FromResult(new FileOperationResult(paths.Count, 0, 0, [])
            {
                Changes = [.. paths.Select(p => new FileOperationChange(p, p, $@"C:\$RECYCLE.BIN\$R{Path.GetFileName(p)}"))]
            });
        }

        public ValueTask<FileOperationResult> RenameAsync(string path, string newName, CancellationToken ct = default)
        {
            Renames.Add((path, newName));
            var target = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, newName);
            return ValueTask.FromResult(new FileOperationResult(1, 0, 0, [])
            {
                Changes = [new FileOperationChange(path, target)]
            });
        }

        public ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default)
            => ValueTask.FromResult(Path.Combine(parentPath, name));

        public ValueTask<bool> CanMoveToRecycleBinAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(true);

        private static FileOperationResult Success(IReadOnlyList<string> sources, string destination)
            => new(sources.Count, 0, 0, [])
            {
                Changes = [.. sources.Select(s =>
                    new FileOperationChange(s, Path.Combine(destination, Path.GetFileName(s))))]
            };
    }

    private sealed class RecordingShellEnumerator : IShellFolderEnumerator
    {
        public List<(string ItemPath, string? Destination)> Restores { get; } = [];

        public ValueTask<bool> RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default)
        {
            Restores.Add((itemPath, destinationPath));
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask EmptyRecycleBinAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public bool HasRecycleBinItems() => false;

#pragma warning disable CS0067 // Required by the interface; nothing in these tests raises it.
        public event EventHandler? RecycleBinChanged;
#pragma warning restore CS0067

        public void StartRecycleBinWatcher() { }

        public void StopRecycleBinWatcher() { }
    }

    private sealed class UnusedArchiveProvider : IArchiveProvider
    {
        public bool IsArchiveFile(string path) => false;

        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default)
            => throw new NotSupportedException();

        public ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default)
            => throw new NotSupportedException();

        public ValueTask CreateZipAsync(IReadOnlyList<string> sourcePaths, string destinationZipPath, CancellationToken token = default)
            => throw new NotSupportedException();

        public ValueTask ExtractArchiveToDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken token = default)
            => throw new NotSupportedException();

        public ValueTask ExtractVirtualEntriesAsync(IReadOnlyList<string> virtualPaths, string destinationDirectory, CancellationToken token = default)
            => throw new NotSupportedException();

        public void CleanupExtractedFiles() { }
    }

    private sealed class StubReporter : IFileOperationReporter
    {
        public bool IsBusy { get; set; }
        public CancellationToken CancellationToken => CancellationToken.None;
        public void WaitIfPaused(CancellationToken cancellationToken) { }
        public void Begin(FileOperationKind kind, int totalItems, string title) { }
        public void Report(FileOperationProgress progress) { }
        public void Complete(FileOperationKind kind, int itemCount, string message) { }
        public void Fail(string message) { }
        public void Cancelled(string message) { }
    }

    private sealed class SilentDialogs : IUserDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowOperationSummaryAsync(FileOperationResult result, string operationName) => Task.CompletedTask;
        public Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll)
            => Task.FromResult<FileConflictResolution?>(null);
    }
}
