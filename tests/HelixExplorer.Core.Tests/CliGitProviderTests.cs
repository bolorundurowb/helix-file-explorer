using System.Diagnostics;
using HelixExplorer.Core.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.Core.Tests;

public sealed class CliGitProviderTests
{
    [Fact]
    public async Task CheckoutBranchAsync_UnknownBranch_ReturnsFalse()
    {
        // CORE-2/CORE-3 regression: git exits non-zero for an unknown branch rather than throwing,
        // so treating "did not throw" as success silently reported a failed checkout as if the
        // working tree had actually switched branches.
        if (!IsGitAvailable())
            return;

        var root = CreateTempDirectory();
        try
        {
            RunGitCommand(root, "init");
            var provider = new CliGitProvider(NullLogger<CliGitProvider>.Instance);

            var succeeded = await provider.CheckoutBranchAsync(root, "definitely-does-not-exist-branch");

            succeeded.Must().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static bool IsGitAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            return process is not null && process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGitCommand(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        })!;
        process.WaitForExit(10000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindRepoRoot_NullOrEmptyPath_ReturnsNull(string? path)
    {
        CliGitProvider.FindRepoRoot(path).Must().BeNull();
    }

    [Fact]
    public void FindRepoRoot_DirectoryWithGitSubdirectory_ReturnsThatDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));

            CliGitProvider.FindRepoRoot(root).Must().Be(root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindRepoRoot_NestedDirectoryUnderRepo_WalksUpToRepoRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var nested = Path.Combine(root, "src", "sub");
            Directory.CreateDirectory(nested);

            CliGitProvider.FindRepoRoot(nested).Must().Be(root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindRepoRoot_GitWorktreeFile_IsTreatedAsRepoRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: /somewhere/else\n");

            CliGitProvider.FindRepoRoot(root).Must().Be(root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindRepoRoot_FilePathInsideRepo_ResolvesContainingRepoRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var filePath = Path.Combine(root, "notes.txt");
            File.WriteAllText(filePath, "hello");

            CliGitProvider.FindRepoRoot(filePath).Must().Be(root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindRepoRoot_NoGitFolderInAncestry_ReturnsNull()
    {
        var root = CreateTempDirectory();
        try
        {
            CliGitProvider.FindRepoRoot(root).Must().BeNull();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void IsInsideRepository_DirectoryWithGitFolder_ReturnsTrue()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var provider = new CliGitProvider(NullLogger<CliGitProvider>.Instance);

            provider.IsInsideRepository(root).Must().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void IsInsideRepository_DirectoryWithoutGitFolder_ReturnsFalse()
    {
        var root = CreateTempDirectory();
        try
        {
            var provider = new CliGitProvider(NullLogger<CliGitProvider>.Instance);

            provider.IsInsideRepository(root).Must().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
        => Directory.CreateTempSubdirectory("helix-git-tests-").FullName;

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
}
