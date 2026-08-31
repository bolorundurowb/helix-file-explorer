using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

public class WinFileOperationServiceTests
{
    [Fact]
    public async Task CopyAsync_SamePathReplace_DoesNotDeleteSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var file = Path.Combine(root, "a.txt");
            await File.WriteAllTextAsync(file, "unique-content");

            var service = CreateService();
            var result = await service.CopyAsync(
                [file],
                root,
                conflicts: new FixedConflictResolver(FileConflictChoice.Replace));

            File.Exists(file).Must().BeTrue();
            (await File.ReadAllTextAsync(file)).Must().Be("unique-content");
            result.Failed.Must().Be(0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MoveAsync_SamePathDirectoryReplace_DoesNotDeleteSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var folder = Path.Combine(root, "folder");
            Directory.CreateDirectory(folder);
            var nested = Path.Combine(folder, "keep.txt");
            await File.WriteAllTextAsync(nested, "keep-me");

            var service = CreateService();
            var result = await service.MoveAsync(
                [folder],
                root,
                conflicts: new FixedConflictResolver(FileConflictChoice.Replace));

            Directory.Exists(folder).Must().BeTrue();
            File.Exists(nested).Must().BeTrue();
            result.Failed.Must().Be(0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MoveAsync_DirectoryMerge_CombinesTreesAndRemovesSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var dest = Path.Combine(root, "dest", "source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(dest);
            await File.WriteAllTextAsync(Path.Combine(source, "only-source.txt"), "s");
            await File.WriteAllTextAsync(Path.Combine(dest, "only-dest.txt"), "d");

            var service = CreateService();
            var result = await service.MoveAsync(
                [source],
                Path.Combine(root, "dest"),
                conflicts: new FixedConflictResolver(FileConflictChoice.Merge));

            result.Failed.Must().Be(0);
            // Both sides survive the merge.
            File.Exists(Path.Combine(dest, "only-dest.txt")).Must().BeTrue();
            File.Exists(Path.Combine(dest, "only-source.txt")).Must().BeTrue();
            // The source tree is removed after a merge-move.
            Directory.Exists(source).Must().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_DirectoryReplace_RemovesDestinationOnlyFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var dest = Path.Combine(root, "dest", "source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(dest);
            await File.WriteAllTextAsync(Path.Combine(source, "only-source.txt"), "s");
            await File.WriteAllTextAsync(Path.Combine(dest, "only-dest.txt"), "d");

            var service = CreateService();
            var result = await service.CopyAsync(
                [source],
                Path.Combine(root, "dest"),
                conflicts: new FixedConflictResolver(FileConflictChoice.Replace));

            result.Failed.Must().Be(0);
            File.Exists(Path.Combine(dest, "only-source.txt")).Must().BeTrue();
            File.Exists(Path.Combine(dest, "only-dest.txt")).Must().BeFalse();
            File.Exists(Path.Combine(source, "only-source.txt")).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_DirectoryMerge_NestedFileSkip_KeepsDestinationFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var dest = Path.Combine(root, "dest", "source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(dest);
            await File.WriteAllTextAsync(Path.Combine(source, "both.txt"), "from-source");
            await File.WriteAllTextAsync(Path.Combine(dest, "both.txt"), "from-dest");
            await File.WriteAllTextAsync(Path.Combine(dest, "only-dest.txt"), "d");
            await File.WriteAllTextAsync(Path.Combine(source, "only-source.txt"), "s");

            var service = CreateService();
            var result = await service.CopyAsync(
                [source],
                Path.Combine(root, "dest"),
                conflicts: new DirectoryMergeThenFileSkipResolver());

            result.Failed.Must().Be(0);
            File.Exists(Path.Combine(dest, "only-dest.txt")).Must().BeTrue();
            File.Exists(Path.Combine(dest, "only-source.txt")).Must().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(dest, "both.txt"))).Must().Be("from-dest");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MoveAsync_AcrossVolumes_WhenSecondDriveExists()
    {
        var tempRoot = Path.GetPathRoot(Path.GetTempPath());
        var other = DriveInfo.GetDrives()
            .FirstOrDefault(d => d.IsReady
                                 && d.DriveType is DriveType.Fixed or DriveType.Removable
                                 && !string.Equals(d.Name, tempRoot, StringComparison.OrdinalIgnoreCase));
        if (other is null)
            return;

        var sourceRoot = CreateTempDirectory();
        var destParent = Path.Combine(other.Name, "helix-fileops-crossvol-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(sourceRoot, "moved-tree");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "x");
            Directory.CreateDirectory(destParent);

            var service = CreateService();
            var result = await service.MoveAsync([source], destParent);

            result.Failed.Must().Be(0);
            Directory.Exists(source).Must().BeFalse();
            File.Exists(Path.Combine(destParent, "moved-tree", "file.txt")).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
            TryDeleteDirectory(destParent);
        }
    }

    [Fact]
    public async Task CopyAsync_DirectoryMerge_KeepsBothSidesAndLeavesSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var dest = Path.Combine(root, "dest", "source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(dest);
            await File.WriteAllTextAsync(Path.Combine(source, "only-source.txt"), "s");
            await File.WriteAllTextAsync(Path.Combine(dest, "only-dest.txt"), "d");

            var service = CreateService();
            var result = await service.CopyAsync(
                [source],
                Path.Combine(root, "dest"),
                conflicts: new FixedConflictResolver(FileConflictChoice.Merge));

            result.Failed.Must().Be(0);
            File.Exists(Path.Combine(dest, "only-dest.txt")).Must().BeTrue();
            File.Exists(Path.Combine(dest, "only-source.txt")).Must().BeTrue();
            // A copy-merge leaves the source in place.
            File.Exists(Path.Combine(source, "only-source.txt")).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_DirectoryIntoOwnDescendant_FailsWithoutNesting()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var child = Path.Combine(source, "child");
            Directory.CreateDirectory(child);
            await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "x");

            var service = CreateService();
            var result = await service.CopyAsync([source], child);

            result.Failed.Must().Be(1);
            result.Failures[0].Message.Must().Contain("itself");
            Directory.Exists(Path.Combine(child, "source", "child", "source")).Must().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MoveAsync_DirectoryIntoOwnDescendant_Fails()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var child = Path.Combine(source, "child");
            Directory.CreateDirectory(child);

            var service = CreateService();
            var result = await service.MoveAsync([source], child);

            result.Failed.Must().Be(1);
            Directory.Exists(source).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static WinFileOperationService CreateService()
        => new(NullLogger<WinFileOperationService>.Instance);

    [Fact]
    public async Task CopyAsync_RecordsChangeWithUniquifiedDestination()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var file = Path.Combine(sourceDir, "a.txt");
            await File.WriteAllTextAsync(file, "x");
            await File.WriteAllTextAsync(Path.Combine(destDir, "a.txt"), "existing");

            var service = CreateService();
            var result = await service.CopyAsync(
                [file],
                destDir,
                conflicts: new FixedConflictResolver(FileConflictChoice.KeepBoth));

            // Undo has to recycle the uniquified copy, not the pre-existing "a.txt" it was renamed around.
            result.Changes.Count.Must().Be(1);
            result.Changes[0].SourcePath.Must().Be(file);
            result.Changes[0].DestinationPath.Must().Be(Path.Combine(destDir, "a (1).txt"));
            result.Changes[0].DestinationPath.Must().NotBe(Path.Combine(destDir, "a.txt"));
            File.Exists(result.Changes[0].DestinationPath).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_DirectoryRecordsOnlyTopLevelChange()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            var nested = Directory.CreateDirectory(Path.Combine(sourceDir, "nested")).FullName;
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "one.txt"), "1");
            await File.WriteAllTextAsync(Path.Combine(nested, "two.txt"), "2");

            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var service = CreateService();
            var result = await service.CopyAsync([sourceDir], destDir);

            // One change for the folder, never one per file inside it: undoing a big paste should
            // recycle a single folder rather than enumerate the whole tree.
            result.Changes.Count.Must().Be(1);
            result.Changes[0].DestinationPath.Must().Be(Path.Combine(destDir, "tree"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_SkippedSourceRecordsNoChange()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var file = Path.Combine(sourceDir, "a.txt");
            await File.WriteAllTextAsync(file, "x");
            await File.WriteAllTextAsync(Path.Combine(destDir, "a.txt"), "existing");

            var service = CreateService();
            var result = await service.CopyAsync(
                [file],
                destDir,
                conflicts: new FixedConflictResolver(FileConflictChoice.Skip));

            result.Skipped.Must().Be(1);
            result.Changes.Count.Must().Be(0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_MergeIsFlaggedSoItIsNeverUndoable()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src", "shared")).FullName;
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "new.txt"), "new");

            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;
            var existing = Directory.CreateDirectory(Path.Combine(destDir, "shared")).FullName;
            await File.WriteAllTextAsync(Path.Combine(existing, "old.txt"), "old");

            var service = CreateService();
            var result = await service.CopyAsync(
                [sourceDir],
                destDir,
                conflicts: new FixedConflictResolver(FileConflictChoice.Merge));

            result.UsedMerge.Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MoveAsync_RecordsSourceAndDestination()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "dest")).FullName;

            var file = Path.Combine(sourceDir, "moved.txt");
            await File.WriteAllTextAsync(file, "x");

            var service = CreateService();
            var result = await service.MoveAsync([file], destDir);

            result.Changes.Count.Must().Be(1);
            result.Changes[0].SourcePath.Must().Be(file);
            result.Changes[0].DestinationPath.Must().Be(Path.Combine(destDir, "moved.txt"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RenameAsync_RecordsOldAndNewPath()
    {
        var root = CreateTempDirectory();
        try
        {
            var file = Path.Combine(root, "before.txt");
            await File.WriteAllTextAsync(file, "x");

            var service = CreateService();
            var result = await service.RenameAsync(file, "after.txt");

            result.Changes.Count.Must().Be(1);
            result.Changes[0].SourcePath.Must().Be(file);
            result.Changes[0].DestinationPath.Must().Be(Path.Combine(root, "after.txt"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreateFolderAsync_ReturnsUniquifiedPath()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = CreateService();

            var first = await service.CreateFolderAsync(root, "New Folder");
            var second = await service.CreateFolderAsync(root, "New Folder");

            // Redo of a new folder recreates the recorded path, so the caller must keep this return
            // value rather than assuming the name it asked for.
            first.Must().Be(Path.Combine(root, "New Folder"));
            second.Must().NotBe(first);
            Directory.Exists(second).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
        => Directory.CreateTempSubdirectory("helix-fileops-tests-").FullName;

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

    private sealed class FixedConflictResolver(FileConflictChoice choice) : IFileConflictResolver
    {
        public bool ApplyToAllChosen => false;

        public Task<FileConflictChoice?> ResolveAsync(FileConflictInfo conflict)
            => Task.FromResult<FileConflictChoice?>(choice);

        public FileConflictChoice? ResolveSync(FileConflictInfo conflict) => choice;
    }

    private sealed class DirectoryMergeThenFileSkipResolver : IFileConflictResolver
    {
        public bool ApplyToAllChosen => false;

        public Task<FileConflictChoice?> ResolveAsync(FileConflictInfo conflict)
            => Task.FromResult(ResolveSync(conflict));

        public FileConflictChoice? ResolveSync(FileConflictInfo conflict)
            => conflict.IsDirectory ? FileConflictChoice.Merge : FileConflictChoice.Skip;
    }
}
