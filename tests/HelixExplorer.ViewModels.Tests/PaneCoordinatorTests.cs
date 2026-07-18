using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

public class PaneSelectionModelTests
{
    private static IReadOnlyList<EntryItemViewModel> CreateEntries(int count)
    {
        var entries = new List<EntryItemViewModel>(count);
        for (var i = 0; i < count; i++)
        {
            var path = $@"C:\folder\file{i}.txt";
            entries.Add(new EntryItemViewModel(
                new FileSystemEntry(path, $"file{i}.txt", false, 10, DateTime.UtcNow, ".txt"),
                showFileExtensions: true));
        }

        return entries;
    }

    [Fact]
    public void SelectRange_SelectsInclusiveSpan()
    {
        var entries = CreateEntries(5);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[1], entries);
        model.SelectRange(entries[3], entries);

        model.SelectedCount.Must().Be(3);
        model.SelectedEntries[0].Must().Be(entries[1]);
        model.SelectedEntries[^1].Must().Be(entries[3]);
    }

    [Fact]
    public void SelectRange_RepeatedExtensionsPreserveOriginalAnchorAndActiveEndpoint()
    {
        var entries = CreateEntries(6);
        var model = new PaneSelectionModel();

        model.SelectSingle(entries[2], entries);
        model.SelectRange(entries[5], entries);
        model.SelectRange(entries[4], entries);
        model.SelectRange(entries[1], entries);

        model.SelectedCount.Must().Be(2);
        model.SelectedEntries[0].Must().Be(entries[1]);
        model.SelectedEntries[1].Must().Be(entries[2]);
        model.SelectedEntry.Must().Be(entries[1]);
    }

    [Fact]
    public void SelectByBounds_AdditiveKeepsExistingSelection()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[0], entries);
        model.SelectByBounds([entries[2], entries[3]], entries, additive: true);

        model.SelectedCount.Must().Be(3);
        model.SelectedEntries.Must().Contain(entries[0]);
        model.SelectedEntries.Must().Contain(entries[2]);
        model.SelectedEntries.Must().Contain(entries[3]);
    }

    [Fact]
    public void SelectByBounds_NonAdditiveReplacesSelectionAndAnchorsRangeAtFirstHit()
    {
        var entries = CreateEntries(5);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[0], entries);

        model.SelectByBounds([entries[1], entries[2]], entries, additive: false);
        model.SelectRange(entries[4], entries);

        model.SelectedCount.Must().Be(4);
        model.SelectedEntries.Must().NotContain(entries[0]);
        model.SelectedEntries[0].Must().Be(entries[1]);
        model.SelectedEntries[^1].Must().Be(entries[4]);
    }

    [Fact]
    public void SelectAll_SelectsEveryEntry()
    {
        var entries = CreateEntries(3);
        var model = new PaneSelectionModel();
        model.SelectAll(entries);

        model.SelectedCount.Must().Be(3);
    }

    [Fact]
    public void Clear_EmptiesSelectionAndResetsAnchor()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[1], entries);
        model.SelectRange(entries[3], entries);

        model.Clear();

        model.SelectedCount.Must().Be(0);
        model.SelectedEntry.Must().BeNull();

        model.SelectRange(entries[2], entries);
        model.SelectedCount.Must().Be(1);
        model.SelectedEntries.Must().Contain(entries[2]);
    }

    [Fact]
    public void Invert_SelectsUnselectedEntriesOnly()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[0], entries);
        model.Toggle(entries[2], entries);

        model.Invert(entries);

        model.SelectedCount.Must().Be(2);
        model.SelectedEntries.Must().Contain(entries[1]);
        model.SelectedEntries.Must().Contain(entries[3]);
        model.SelectedEntries.Must().NotContain(entries[0]);
        model.SelectedEntries.Must().NotContain(entries[2]);
    }

    [Fact]
    public void Toggle_Off_ReanchorsSoSubsequentShiftClickSelectsFromRemainingSelection()
    {
        var entries = CreateEntries(5);
        var model = new PaneSelectionModel();

        model.SelectSingle(entries[1], entries);
        model.Toggle(entries[2], entries);
        model.Toggle(entries[3], entries);
        model.SelectedCount.Must().Be(3);

        model.Toggle(entries[2], entries);
        model.SelectedCount.Must().Be(2);
        model.SelectedEntries.Must().NotContain(entries[2]);

        model.SelectRange(entries[4], entries);

        model.SelectedCount.Must().Be(4);
        model.SelectedEntries.Must().Contain(entries[1]);
        model.SelectedEntries.Must().Contain(entries[2]);
        model.SelectedEntries.Must().Contain(entries[3]);
        model.SelectedEntries.Must().Contain(entries[4]);
    }

    [Fact]
    public void Toggle_OffLastSelected_ClearsAnchor()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();

        model.SelectSingle(entries[2], entries);
        model.Toggle(entries[2], entries);

        model.SelectedCount.Must().Be(0);

        model.SelectRange(entries[1], entries);
        model.SelectedCount.Must().Be(1);
        model.SelectedEntries.Must().Contain(entries[1]);
    }
}

public class PaneNavigationControllerTests
{
    [Fact]
    public void BuildBreadcrumbs_UsesRecycleBinLabel()
    {
        var crumbs = PaneNavigationController.BuildBreadcrumbs(ShellPath.RecycleBin);

        crumbs.Must().HaveCount(1);
        crumbs[0].DisplayName.Must().Be("Recycle Bin");
        crumbs[0].IsLast.Must().BeTrue();
    }

    [Fact]
    public void RecordForward_EnablesBackNavigation()
    {
        var navigation = new PaneNavigationController(new FakeFileSystem(), new FakeArchive());
        var transition = navigation.RecordForward(@"C:\", @"C:\Users\");

        transition.Path.Must().Be(@"C:\Users\");
        transition.CanGoBack.Must().BeTrue();
        transition.CanGoForward.Must().BeFalse();
    }

    private sealed class FakeFileSystem : IFileSystemProvider
    {
        public string ResolvePath(string path) => path;

        public ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(DirectoryListing.Empty);

        public ValueTask<SearchResult> SearchRecursiveAsync(string path, string query, SearchOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SearchResult.Empty);

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path) => false;
    }

    private sealed class FakeArchive : IArchiveProvider
    {
        public bool IsArchiveFile(string path) => false;

        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

        public ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default)
            => ValueTask.FromResult<string?>(null);

        public ValueTask CreateZipAsync(IReadOnlyList<string> sourcePaths, string destinationZipPath, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public ValueTask ExtractArchiveToDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public ValueTask ExtractVirtualEntriesAsync(IReadOnlyList<string> virtualPaths, string destinationDirectory, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public void CleanupExtractedFiles()
        {
        }
    }
}

public class PaneFileOperationPasteAvailabilityTests
{
    [Fact]
    public async Task HasPastePayloadAsync_FalseWhenClipboardEmpty()
    {
        var clipboard = new InternalClipboardService();
        var coordinator = CreateCoordinator(clipboard, osPayload: null);

        (await coordinator.HasPastePayloadAsync()).Must().BeFalse();
    }

    [Fact]
    public async Task HasPastePayloadAsync_FalseWhenInternalPathsDoNotExist()
    {
        var clipboard = new InternalClipboardService();
        clipboard.SetCopy([$@"C:\helix-missing-{Guid.NewGuid():N}.txt"], @"C:\");
        var coordinator = CreateCoordinator(clipboard, osPayload: null);

        (await coordinator.HasPastePayloadAsync()).Must().BeFalse();
    }

    [Fact]
    public async Task HasPastePayloadAsync_TrueForValidInternalFilePayload()
    {
        using var temp = TempClipboardPaths.CreateFile();
        var clipboard = new InternalClipboardService();
        clipboard.SetCopy([temp.Path], temp.Directory);
        var coordinator = CreateCoordinator(clipboard, osPayload: null);

        (await coordinator.HasPastePayloadAsync()).Must().BeTrue();
    }

    [Fact]
    public async Task HasPastePayloadAsync_TrueForValidInternalFolderPayload()
    {
        using var temp = TempClipboardPaths.CreateDirectory();
        var clipboard = new InternalClipboardService();
        clipboard.SetCopy([temp.Path], Path.GetDirectoryName(temp.Path)!);
        var coordinator = CreateCoordinator(clipboard, osPayload: null);

        (await coordinator.HasPastePayloadAsync()).Must().BeTrue();
    }

    [Fact]
    public async Task HasPastePayloadAsync_TrueForValidOsFilePayloadWhenInternalEmpty()
    {
        using var temp = TempClipboardPaths.CreateFile();
        var clipboard = new InternalClipboardService();
        var coordinator = CreateCoordinator(
            clipboard,
            osPayload: ([temp.Path], ClipboardOperation.Copy));

        (await coordinator.HasPastePayloadAsync()).Must().BeTrue();
    }

    [Fact]
    public async Task HasPastePayloadAsync_FalseForOsPayloadWithMissingPaths()
    {
        var clipboard = new InternalClipboardService();
        var coordinator = CreateCoordinator(
            clipboard,
            osPayload: ([$@"C:\helix-missing-{Guid.NewGuid():N}.txt"], ClipboardOperation.Copy));

        (await coordinator.HasPastePayloadAsync()).Must().BeFalse();
    }

    private static PaneFileOperationCoordinator CreateCoordinator(
        IClipboardService clipboard,
        (IReadOnlyList<string> Paths, ClipboardOperation Operation)? osPayload)
    {
        return new PaneFileOperationCoordinator(
            new UnusedFileOps(),
            clipboard,
            new FakeOsClipboard(osPayload),
            new UnusedDialogs(),
            new UnusedReporter(),
            NullLogger<PaneFileOperationCoordinator>.Instance);
    }

    private sealed class FakeOsClipboard(
        (IReadOnlyList<string> Paths, ClipboardOperation Operation)? payload) : IOsFileClipboard
    {
        public Task SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?> TryGetFilesAsync(CancellationToken ct = default)
            => Task.FromResult(payload);
    }

    private sealed class UnusedFileOps : IFileOperationService
    {
        public ValueTask<FileOperationResult> CopyAsync(IReadOnlyList<string> sources, string destination, IProgress<FileOperationProgress>? progress = null, IFileConflictResolver? conflicts = null, CancellationToken ct = default, IFileOperationControl? control = null)
            => throw new NotSupportedException();

        public ValueTask<FileOperationResult> MoveAsync(IReadOnlyList<string> sources, string destination, IProgress<FileOperationProgress>? progress = null, IFileConflictResolver? conflicts = null, CancellationToken ct = default, IFileOperationControl? control = null)
            => throw new NotSupportedException();

        public ValueTask<FileOperationResult> DeleteAsync(IReadOnlyList<string> paths, bool permanently, IProgress<FileOperationProgress>? progress = null, CancellationToken ct = default, IFileOperationControl? control = null)
            => throw new NotSupportedException();

        public ValueTask<FileOperationResult> RenameAsync(string path, string newName, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask<bool> CanMoveToRecycleBinAsync(string path, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class UnusedDialogs : IUserDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => throw new NotSupportedException();
        public Task ShowErrorAsync(string title, string message) => throw new NotSupportedException();
        public Task ShowOperationSummaryAsync(FileOperationResult result, string operationName) => throw new NotSupportedException();
        public Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll) => throw new NotSupportedException();
    }

    private sealed class UnusedReporter : IFileOperationReporter
    {
        public CancellationToken CancellationToken => CancellationToken.None;
        public void WaitIfPaused(CancellationToken cancellationToken) { }
        public void Begin(FileOperationKind kind, int totalItems, string title) { }
        public void Report(FileOperationProgress progress) { }
        public void Complete(FileOperationKind kind, int itemCount, string message) { }
        public void Fail(string message) { }
        public void Cancelled(string message) { }
    }

    private sealed class TempClipboardPaths : IDisposable
    {
        private readonly string _root;

        private TempClipboardPaths(string root, string path)
        {
            _root = root;
            Path = path;
            Directory = System.IO.Directory.GetParent(path)!.FullName;
        }

        public string Path { get; }
        public string Directory { get; }

        public static TempClipboardPaths CreateFile()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helix-paste-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            var file = System.IO.Path.Combine(root, "item.txt");
            System.IO.File.WriteAllText(file, "x");
            return new TempClipboardPaths(root, file);
        }

        public static TempClipboardPaths CreateDirectory()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helix-paste-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            var folder = System.IO.Path.Combine(root, "folder");
            System.IO.Directory.CreateDirectory(folder);
            return new TempClipboardPaths(root, folder);
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(_root))
                    System.IO.Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp paths.
            }
        }
    }
}

public class PaneFileOperationCoordinatorDropTests
{
    [Fact]
    public void CanDropPath_AllowsMovingFileIntoSiblingFolder()
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            @"C:\workspace\target",
            @"C:\workspace\file.txt",
            isCopy: false);

        allowed.Must().BeTrue();
    }

    [Fact]
    public void CanDropPath_RejectsMovingItemIntoSameFolder()
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            @"C:\workspace",
            @"C:\workspace\file.txt",
            isCopy: false);

        allowed.Must().BeFalse();
    }

    [Fact]
    public void CanDropPath_AllowsCopyingItemIntoSameFolder()
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            @"C:\workspace",
            @"C:\workspace\file.txt",
            isCopy: true);

        allowed.Must().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\workspace\folder", @"C:\workspace\folder")]
    [InlineData(@"C:\workspace\folder\child", @"C:\workspace\folder")]
    public void CanDropPath_RejectsDroppingFolderIntoItselfOrChild(string destination, string source)
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            destination,
            source,
            isCopy: false);

        allowed.Must().BeFalse();
    }
}

public class PaneRenameTests
{
    [Theory]
    [InlineData("document.txt", false, 8)]
    [InlineData("archive.tar.gz", false, 11)]
    [InlineData("README", false, 6)]
    [InlineData(".gitignore", false, 10)]
    [InlineData("Folder.Name", true, 11)]
    public void GetRenameBaseNameLength_SelectsNameWithoutFileExtension(
        string name,
        bool isDirectory,
        int expected)
    {
        PaneViewModel.GetRenameBaseNameLength(name, isDirectory).Must().Be(expected);
    }
}
