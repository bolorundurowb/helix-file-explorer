using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.FileSystem;
using HelixExplorer.Windows.Shell;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.Windows.Tests;

public class WinFileDeleteTests
{
    [Fact]
    public async Task DeleteAsync_Permanent_RemovesFile()
    {
        var root = Directory.CreateTempSubdirectory("helix-delete-tests-").FullName;
        try
        {
            var file = Path.Combine(root, "gone.txt");
            await File.WriteAllTextAsync(file, "x");

            var service = new WinFileOperationService(NullLogger<WinFileOperationService>.Instance);
            var result = await service.DeleteAsync([file], permanently: true);

            result.Failed.Must().Be(0);
            File.Exists(file).Must().BeFalse();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DeleteAsync_Recycle_RemovesFromSourceWhenBinAccepts()
    {
        var root = Directory.CreateTempSubdirectory("helix-recycle-tests-").FullName;
        try
        {
            var file = Path.Combine(root, "recycle-me.txt");
            await File.WriteAllTextAsync(file, "x");

            var service = new WinFileOperationService(NullLogger<WinFileOperationService>.Instance);
            var canRecycle = await ShellFileOperationsHelper.CanMoveToRecycleBinAsync(file, CancellationToken.None);
            if (!canRecycle)
                return;

            var result = await service.DeleteAsync([file], permanently: false);
            result.Failed.Must().Be(0);
            File.Exists(file).Must().BeFalse();

            var change = result.Changes.Single();
            change.SourcePath.Must().Be(file);
            string.IsNullOrWhiteSpace(change.RecycleItemPath).Must().BeFalse();

            using var shell = new WinShellFolderEnumerator(NullLogger<WinShellFolderEnumerator>.Instance);
            var restored = await shell.RestoreAsync(change.RecycleItemPath!, file);
            restored.Must().BeTrue();
            File.Exists(file).Must().BeTrue();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CanMoveToRecycleBinAsync_UncPath_IsFalse()
    {
        var can = await ShellFileOperationsHelper.CanMoveToRecycleBinAsync(@"\\deadhost\share\file.txt", CancellationToken.None);
        can.Must().BeFalse();
    }

    [Fact]
    public async Task CopyAsync_DirectorySymlink_DoesNotFollowTarget()
    {
        var root = Directory.CreateTempSubdirectory("helix-reparse-tests-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            await File.WriteAllTextAsync(Path.Combine(target, "outside.txt"), "must not copy");
            var link = Path.Combine(root, "link");
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            var service = new WinFileOperationService(NullLogger<WinFileOperationService>.Instance);

            var result = await service.CopyAsync([link], destination);

            result.Succeeded.Must().Be(0);
            result.Skipped.Must().Be(1);
            Directory.Exists(Path.Combine(destination, "link")).Must().BeFalse();
            File.Exists(Path.Combine(destination, "link", "outside.txt")).Must().BeFalse();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
