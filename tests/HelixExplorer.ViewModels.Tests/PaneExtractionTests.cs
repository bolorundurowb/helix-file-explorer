using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public class PaneExtractionTests
{
    [Fact]
    public void ListingState_Clear_DropsDirectorySearchAndGitSnapshots()
    {
        var entry = new FileSystemEntry(@"C:\a.txt", "a.txt", false, 1, DateTime.UtcNow, ".txt");
        var state = new PaneListingState
        {
            AllEntries = [entry],
            DirectoryEntries = [entry]
        };

        state.Clear();

        state.AllEntries.Must().BeEmpty();
        state.DirectoryEntries.Must().BeEmpty();
        state.GitSnapshot.Must().Be(GitStatusSnapshot.Empty);
    }

    [Theory]
    [InlineData("file.txt", false, 4)]
    [InlineData("archive.tar.gz", false, 11)]
    [InlineData("folder.txt", true, 10)]
    [InlineData(".gitignore", false, 10)]
    public void RenameController_BaseNameLength_SelectsExpectedStem(
        string name,
        bool isDirectory,
        int expected)
        => PaneRenameController.GetBaseNameLength(name, isDirectory).Must().Be(expected);

    [Fact]
    public void RenameController_Clear_ReleasesEntryBeforeUiStateChanges()
    {
        var entry = new EntryItemViewModel(
            new FileSystemEntry(@"C:\a.txt", "a.txt", false, 1, DateTime.UtcNow, ".txt"));
        var controller = new PaneRenameController();

        controller.Begin([entry]).Must().BeTrue();
        controller.Clear();

        controller.Entry.Must().BeNull();
        entry.IsRenaming.Must().BeFalse();
        entry.RenameText.Must().BeEmpty();
    }

    [Fact]
    public void ArchiveCommands_Snapshot_ReturnsOnlyImmediateNames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"helix-pane-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");
        try
        {
            var names = new PaneArchiveCommands().SnapshotTopLevelNames(root);
            names.Must().Contain("folder");
            names.Must().Contain("file.txt");
            names.Count.Must().Be(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
