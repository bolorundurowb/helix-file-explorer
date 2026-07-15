using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Git;

/// <summary>
/// Git status via the CLI. Repository-root lookups and recent status snapshots are cached so rapid,
/// repeated refreshes for the same repo coalesce instead of spawning a git process each time.
/// </summary>
public sealed class CliGitProvider(ILogger<CliGitProvider> logger) : IGitProvider
{
    private const string GitExe = "git";

    /// <summary>Window during which a repeated status request for the same root reuses the last snapshot.</summary>
    private static readonly TimeSpan StatusCacheTtl = TimeSpan.FromMilliseconds(750);

    private readonly GitStatusCache _statusCache = new(StatusCacheTtl);

    /// <summary>Cache of directory → repository root (null sentinel stored as empty string).</summary>
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
            var output = await RunGitAsync(root, "status --porcelain=v2 -z --branch", cancellationToken)
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

    /// <summary>Repository-root lookup with per-directory caching to avoid repeated upward directory walks.</summary>
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
            var output = await RunGitAsync(root, "branch --format=%(refname:short)", cancellationToken)
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
            await RunGitWithArgsAsync(root, ["checkout", branch], cancellationToken).ConfigureAwait(false);
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

    private static async Task<string> RunGitWithArgsAsync(string workingDir, string[] args, CancellationToken token)
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

        await using var reg = token.Register(static state =>
        {
            try
            {
                ((Process)state!).Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already exited.
            }
        }, process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return stdoutTask.Result;
    }

    private static async Task<string> RunGitAsync(string workingDir, string args, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(GitExe, args)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = false
        };

        if (!process.Start())
            return string.Empty;

        await using var reg = token.Register(static state =>
        {
            try
            {
                ((Process)state!).Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already exited.
            }
        }, process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return stdoutTask.Result;
    }
}
