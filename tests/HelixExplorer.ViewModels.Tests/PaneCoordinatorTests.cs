using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;
using Xunit;

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

        Assert.Equal(3, model.SelectedCount);
        Assert.Equal(entries[1], model.SelectedEntries[0]);
        Assert.Equal(entries[3], model.SelectedEntries[^1]);
    }

    [Fact]
    public void SelectByBounds_AdditiveKeepsExistingSelection()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();
        model.SelectSingle(entries[0], entries);
        model.SelectByBounds([entries[2], entries[3]], entries, additive: true);

        Assert.Equal(3, model.SelectedCount);
        Assert.Contains(entries[0], model.SelectedEntries);
        Assert.Contains(entries[2], model.SelectedEntries);
        Assert.Contains(entries[3], model.SelectedEntries);
    }

    [Fact]
    public void SelectAll_SelectsEveryEntry()
    {
        var entries = CreateEntries(3);
        var model = new PaneSelectionModel();
        model.SelectAll(entries);

        Assert.Equal(3, model.SelectedCount);
    }

    [Fact]
    public void Toggle_Off_ReanchorsSoSubsequentShiftClickSelectsFromRemainingSelection()
    {
        var entries = CreateEntries(5);
        var model = new PaneSelectionModel();

        // Ctrl+click build-up: select 1, then 2, then 3.
        model.SelectSingle(entries[1], entries);
        model.Toggle(entries[2], entries);
        model.Toggle(entries[3], entries);
        Assert.Equal(3, model.SelectedCount);

        // Ctrl+click the middle item OFF. The anchor must move off the now-unselected row.
        model.Toggle(entries[2], entries);
        Assert.Equal(2, model.SelectedCount);
        Assert.DoesNotContain(entries[2], model.SelectedEntries);

        // Shift+click to entries[4]. With the drifted anchor bug this would start at index 2 and
        // drop entries[1]; with the fix it anchors on the first remaining selection (index 1).
        model.SelectRange(entries[4], entries);

        Assert.Equal(4, model.SelectedCount);
        Assert.Contains(entries[1], model.SelectedEntries);
        Assert.Contains(entries[2], model.SelectedEntries);
        Assert.Contains(entries[3], model.SelectedEntries);
        Assert.Contains(entries[4], model.SelectedEntries);
    }

    [Fact]
    public void Toggle_OffLastSelected_ClearsAnchor()
    {
        var entries = CreateEntries(4);
        var model = new PaneSelectionModel();

        model.SelectSingle(entries[2], entries);
        model.Toggle(entries[2], entries);

        Assert.Equal(0, model.SelectedCount);

        // With no anchor, a shift-click should behave like a single selection at the target.
        model.SelectRange(entries[1], entries);
        Assert.Equal(1, model.SelectedCount);
        Assert.Contains(entries[1], model.SelectedEntries);
    }
}

public class PaneNavigationControllerTests
{
    [Fact]
    public void BuildBreadcrumbs_UsesRecycleBinLabel()
    {
        var crumbs = PaneNavigationController.BuildBreadcrumbs(ShellPath.RecycleBin);

        Assert.Single(crumbs);
        Assert.Equal("Recycle Bin", crumbs[0].DisplayName);
        Assert.True(crumbs[0].IsLast);
    }

    [Fact]
    public void RecordForward_EnablesBackNavigation()
    {
        var navigation = new PaneNavigationController(new FakeFileSystem(), new FakeArchive());
        var transition = navigation.RecordForward(@"C:\", @"C:\Users\");

        Assert.Equal(@"C:\Users\", transition.Path);
        Assert.True(transition.CanGoBack);
        Assert.False(transition.CanGoForward);
    }

    private sealed class FakeFileSystem : IFileSystemProvider
    {
        public string ResolvePath(string path) => path;

        public ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(DirectoryListing.Empty);

        public ValueTask<IReadOnlyList<FileSystemEntry>> SearchRecursiveAsync(string path, string query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

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

        Assert.True(allowed);
    }

    [Fact]
    public void CanDropPath_RejectsMovingItemIntoSameFolder()
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            @"C:\workspace",
            @"C:\workspace\file.txt",
            isCopy: false);

        Assert.False(allowed);
    }

    [Fact]
    public void CanDropPath_AllowsCopyingItemIntoSameFolder()
    {
        var allowed = PaneFileOperationCoordinator.CanDropPath(
            @"C:\workspace",
            @"C:\workspace\file.txt",
            isCopy: true);

        Assert.True(allowed);
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

        Assert.False(allowed);
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
        Assert.Equal(expected, PaneViewModel.GetRenameBaseNameLength(name, isDirectory));
    }
}
