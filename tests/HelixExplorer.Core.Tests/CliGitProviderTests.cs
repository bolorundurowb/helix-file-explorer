using System.Diagnostics;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
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

    [Fact]
    public async Task GetStatusAsync_UsesInjectedProcessRunner()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var runner = new FakeProcessRunner(new ProcessRunResult(
                0,
                "# branch.head main\0? deterministic.txt\0",
                string.Empty));
            var provider = new CliGitProvider(NullLogger<CliGitProvider>.Instance, runner);

            var status = await provider.GetStatusAsync(root);

            runner.Calls.Must().Be(1);
            runner.LastStartInfo!.ArgumentList.Must().Contain("--porcelain=v2");
            runner.LastStartInfo.Environment["GIT_TERMINAL_PROMPT"].Must().Be("0");
            status.GetStatusForPath(Path.Combine(root, "deterministic.txt")).Must().Be(GitFileStatus.Untracked);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RepositoryRootCache_RemainsBounded()
    {
        var root = CreateTempDirectory();
        try
        {
            var provider = new CliGitProvider(NullLogger<CliGitProvider>.Instance);
            for (var i = 0; i < 600; i++)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, i.ToString())).FullName;
                provider.IsInsideRepository(directory).Must().BeFalse();
            }

            provider.RootCacheCount.Must().BeLessThanOrEqualTo(512);
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

    private sealed class FakeProcessRunner(ProcessRunResult result) : IProcessRunner
    {
        public int Calls { get; private set; }
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan hardTimeout,
            bool killOnCancellation,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastStartInfo = startInfo;
            return Task.FromResult(result);
        }
    }
}
