using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;

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
    public void SelectAll_SelectsEveryEntry()
    {
        var entries = CreateEntries(3);
        var model = new PaneSelectionModel();
        model.SelectAll(entries);

        model.SelectedCount.Must().Be(3);
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
