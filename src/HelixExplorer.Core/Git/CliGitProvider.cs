using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Git;

/// <summary>
/// Repository-root lookups and status snapshots are cached so rapid refreshes coalesce
/// instead of spawning a git process each time.
/// </summary>
public sealed class CliGitProvider(ILogger<CliGitProvider> logger) : IGitProvider
{
    private const string GitExe = "git";

    private static readonly TimeSpan StatusCacheTtl = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Read-only probes must not refresh the index. Without this, cancelling an in-flight
    /// <c>git status</c> (Process.Kill on navigate/refresh) can leave <c>.git/index.lock</c> behind
    /// and block subsequent git commands in the user's repo.
    /// </summary>
    private static readonly string[] StatusArgs =
        ["--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch"];

    private static readonly string[] ListBranchesArgs =
        ["--no-optional-locks", "branch", "--format=%(refname:short)"];

    private readonly GitStatusCache _statusCache = new(StatusCacheTtl);

    /// <summary>Null roots are stored as empty string so negative lookups stay cached.</summary>
    private readonly ConcurrentDictionary<string, string?> _rootCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInsideRepository(string path) => ResolveRepoRoot(path) is not null;

    public async ValueTask<GitStatusSnapshot> GetStatusAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = ResolveRepoRoot(path);
        if (root is null)
            return GitStatusSnapshot.Empty;

        if (_statusCache.TryGet(root, out var cached))
            return cached;

        try
        {
            var output = await RunGitWithArgsAsync(root, StatusArgs, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = GitPorcelainParser.Parse(output, root);
            _statusCache.Store(root, snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git status query failed for '{Path}'", path);
            return GitStatusSnapshot.Empty;
        }
    }

    /// <summary>Per-directory cache avoids repeated upward directory walks.</summary>
    private string? ResolveRepoRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var key = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;

        if (_rootCache.TryGetValue(key, out var cachedRoot))
            return string.IsNullOrEmpty(cachedRoot) ? null : cachedRoot;

        var root = FindRepoRoot(path);
        // Store empty string as the "not a repo" sentinel so negative lookups are cached too.
        _rootCache[key] = root ?? string.Empty;
        return root;
    }

    public async ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = ResolveRepoRoot(path);
        if (root is null)
            return Array.Empty<string>();

        try
        {
            var output = await RunGitWithArgsAsync(root, ListBranchesArgs, cancellationToken)
                .ConfigureAwait(false);
            var list = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                var branch = line.Trim();
                if (branch.Length > 0)
                    list.Add(branch);
            }

            return list;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git branch list failed for '{Path}'", path);
            return Array.Empty<string>();
        }
    }

    public async ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken cancellationToken = default)
    {
        var root = ResolveRepoRoot(path);
        if (root is null || string.IsNullOrWhiteSpace(branch))
            return false;

        try
        {
            // Checkout must update the index; do not Kill on cancel or a mid-write death leaves index.lock.
            await RunGitWithArgsAsync(root, ["checkout", branch], cancellationToken, killOnCancel: false)
                .ConfigureAwait(false);
            // Working tree changed: drop any cached status so the next refresh reflects the new branch.
            _statusCache.Invalidate(root);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git checkout failed for branch '{Branch}' in '{Path}'", branch, path);
            return false;
        }
    }

    internal static string? FindRepoRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            var gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static async Task<string> RunGitWithArgsAsync(
        string workingDir,
        string[] args,
        CancellationToken token,
        bool killOnCancel = true)
    {
        var psi = new ProcessStartInfo(GitExe)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

        if (!process.Start())
            return string.Empty;

        CancellationTokenRegistration reg = default;
        if (killOnCancel)
        {
            // Safe for read-only probes that use --no-optional-locks. Do not enable for index writers.
            reg = token.Register(static state =>
            {
                try
                {
                    ((Process)state!).Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore races where the process finished between cancel and Kill.
                }
            }, process);
        }

        await using (reg)
        {
            // Do not cancel stream reads: a cancelled ReadToEnd can leave the child blocked on a full pipe.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            // Index writers must run to completion; only status/list may abandon via Kill + cancelled wait.
            var waitToken = killOnCancel ? token : CancellationToken.None;
            await process.WaitForExitAsync(waitToken).ConfigureAwait(false);
            return stdoutTask.Result;
        }
    }
}
