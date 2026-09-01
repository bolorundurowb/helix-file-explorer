using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Git;

/// <summary>
/// Repository-root lookups and status snapshots are cached so rapid refreshes coalesce
/// instead of spawning a git process each time.
/// </summary>
public sealed class CliGitProvider : IGitProvider
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
    private readonly ILogger<CliGitProvider> _logger;
    private readonly IProcessRunner _processRunner;

    /// <summary>Null roots are stored as empty string so negative lookups stay cached.</summary>
    private readonly ConcurrentDictionary<string, string?> _rootCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _rootInsertionOrder = new();

    internal int RootCacheCount => _rootCache.Count;

    public CliGitProvider(ILogger<CliGitProvider> logger, IProcessRunner? processRunner = null)
    {
        _logger = logger;
        _processRunner = processRunner ?? new ProcessRunner();
    }

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
            var result = await RunGitWithArgsAsync(root, StatusArgs, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                _logger.LogWarning("git status exited {ExitCode} for '{Path}': {Stderr}", result.ExitCode, path, result.StandardError);

            var snapshot = GitPorcelainParser.Parse(result.StandardOutput, root);
            _statusCache.Store(root, snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Deliberately broad: a git-status refresh is best-effort UI chrome, not a required
        // operation, and the git process/parsing pipeline can fail in many ways (git missing, repo
        // corrupted, unexpected porcelain output, ...). Degrading to an empty snapshot and logging
        // is strictly better than letting any one of them crash pane refresh.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git status query failed for '{Path}'", path);
            return GitStatusSnapshot.Empty;
        }
#pragma warning restore CA1031
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
        if (_rootCache.TryAdd(key, root ?? string.Empty))
            _rootInsertionOrder.Enqueue(key);
        else
            _rootCache[key] = root ?? string.Empty;
        while (_rootCache.Count > 512 && _rootInsertionOrder.TryDequeue(out var oldest))
            _rootCache.TryRemove(oldest, out _);
        return root;
    }

    public async ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = ResolveRepoRoot(path);
        if (root is null)
            return Array.Empty<string>();

        try
        {
            var result = await RunGitWithArgsAsync(root, ListBranchesArgs, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                _logger.LogWarning("git branch exited {ExitCode} for '{Path}': {Stderr}", result.ExitCode, path, result.StandardError);

            var list = new List<string>();
            foreach (var line in result.StandardOutput.Split('\n'))
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
        // Deliberately broad: same rationale as GetStatusAsync above - listing branches is
        // best-effort chrome (e.g. a branch-switch flyout), and an empty list degrades gracefully.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git branch list failed for '{Path}'", path);
            return Array.Empty<string>();
        }
#pragma warning restore CA1031
    }

    public async ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken cancellationToken = default)
    {
        var root = ResolveRepoRoot(path);
        if (root is null || string.IsNullOrWhiteSpace(branch))
            return false;

        try
        {
            // Checkout must update the index; do not Kill on cancel or a mid-write death leaves index.lock.
            var result = await RunGitWithArgsAsync(root, ["checkout", branch], cancellationToken, killOnCancel: false)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                // git does not throw for a failed checkout (conflicting local changes, unknown
                // branch, ...) - it exits non-zero and writes to stderr. Treating any non-throwing
                // completion as success meant a failed checkout was silently reported as if the
                // branch had actually switched.
                _logger.LogError(
                    "Git checkout failed for branch '{Branch}' in '{Path}' (exit {ExitCode}): {Stderr}",
                    branch, path, result.ExitCode, result.StandardError);
                return false;
            }

            // Working tree changed: drop any cached status so the next refresh reflects the new branch.
            _statusCache.Invalidate(root);
            _rootCache.TryRemove(root, out _);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Deliberately broad: same rationale as GetStatusAsync above - a checkout failure is
        // reported to the caller as `false` (already the contract for a non-zero git exit code
        // just above), so an unexpected exception here should degrade the same way, not crash.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git checkout failed for branch '{Branch}' in '{Path}'", branch, path);
            return false;
        }
#pragma warning restore CA1031
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

    /// <summary>
    /// Hard ceiling on any git invocation, independent of the caller's cancellation token. Index
    /// writers ignore that token (killOnCancel: false, so a mid-write kill cannot leave
    /// index.lock behind), so without this a hung git process - e.g. blocked on a credential
    /// prompt the env vars below failed to suppress - would wait forever.
    /// </summary>
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(60);

    private Task<ProcessRunResult> RunGitWithArgsAsync(
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
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        // Never let git fall back to an interactive prompt: there is no terminal for the user to
        // answer one on, and a credential/host-key prompt would otherwise hang the process (and,
        // for index writers, hang forever - see HardTimeout).
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = string.Empty;
        psi.Environment["GCM_INTERACTIVE"] = "never";

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return _processRunner.RunAsync(psi, HardTimeout, killOnCancel, token);
    }
}
