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
}
