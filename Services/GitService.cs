using System.Diagnostics;
using System.IO;
using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>
/// Git status reader built on the git CLI. No native bindings, so it works against
/// any git on PATH and adds zero extra dependencies. A single
/// <c>status --porcelain=v2 --branch</c> call yields the branch, ahead/behind, and
/// per-file state, so one navigation spawns one process instead of three.
/// </summary>
public sealed class GitService : IGitService
{
    private const string GitExe = "git";

    public bool IsInsideRepository(string path) => FindRepoRoot(path) is not null;

    private static string? FindRepoRoot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public async ValueTask<GitStatus> GetStatusAsync(string path, CancellationToken token = default)
    {
        var root = FindRepoRoot(path);
        if (root is null) return GitStatus.Empty;

        try
        {
            var output = await RunGitAsync(root, "status --porcelain=v2 --branch", token).ConfigureAwait(false);
            return Parse(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GitService.GetStatusAsync failed for '{path}': {ex.Message}");
            return GitStatus.Empty;
        }
    }

    /// <summary>Parses <c>git status --porcelain=v2 --branch</c> output.</summary>
    private static GitStatus Parse(string output)
    {
        var branch = string.Empty;
        int ahead = 0, behind = 0, staged = 0, unstaged = 0, untracked = 0;
        var hasUpstream = false;

        foreach (var line in output.Split('\n'))
        {
            var span = line.AsSpan().TrimEnd('\r');
            if (span.IsEmpty) continue;

            if (span[0] == '#')
            {
                if (span.StartsWith("# branch.head "))
                {
                    branch = span["# branch.head ".Length..].ToString();
                }
                else if (span.StartsWith("# branch.upstream "))
                {
                    hasUpstream = true;
                }
                else if (span.StartsWith("# branch.ab "))
                {
                    // Format: "# branch.ab +1 -2"
                    foreach (var part in span["# branch.ab ".Length..].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.StartsWith('+') && int.TryParse(part.AsSpan(1), out var a)) ahead = a;
                        else if (part.StartsWith('-') && int.TryParse(part.AsSpan(1), out var b)) behind = b;
                    }
                }
                continue;
            }

            switch (span[0])
            {
                case '?':
                    untracked++;
                    break;
                case '1':
                case '2':
                case 'u':
                    // "<type> <XY> ..." — XY are the two chars after the leading type + space.
                    if (span.Length >= 4)
                    {
                        var x = span[2];
                        var y = span[3];
                        if (x != '.' && x != ' ') staged++;
                        if (y != '.' && y != ' ') unstaged++;
                    }
                    break;
                // '!' ignored entries are skipped.
            }
        }

        if (string.IsNullOrEmpty(branch)) branch = "(detached)";
        return new GitStatus(branch, staged, unstaged, untracked, hasUpstream, ahead, behind);
    }

    public async ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken token = default)
    {
        var root = FindRepoRoot(path);
        if (root is null) return Array.Empty<string>();
        try
        {
            var output = await RunGitAsync(root, "branch --format=%(refname:short)", token).ConfigureAwait(false);
            var list = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                var b = line.Trim();
                if (b.Length > 0) list.Add(b);
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"GitService.ListBranchesAsync failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public async ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken token = default)
    {
        var root = FindRepoRoot(path);
        if (root is null || string.IsNullOrWhiteSpace(branch)) return false;
        try
        {
            // Quote the branch to be safe against spaces; git ref names disallow most metacharacters.
            await RunGitAsync(root, $"checkout \"{branch}\"", token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"GitService.CheckoutBranchAsync failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> RunGitAsync(string workingDir, string args, CancellationToken token)
    {
        using var runner = new ProcessRunInfo(
            new ProcessStartInfo(GitExe, args)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            });
        return await runner.RunAndGetOutputAsync(token).ConfigureAwait(false);
    }

    /// <summary>Owns a <see cref="Process"/> for the lifetime of a single git invocation.</summary>
    private sealed class ProcessRunInfo : IDisposable
    {
        public Process Process { get; }
        public ProcessRunInfo(ProcessStartInfo si) => Process = new Process { StartInfo = si, EnableRaisingEvents = false };

        public async Task<string> RunAndGetOutputAsync(CancellationToken token)
        {
            if (!Process.Start()) return string.Empty;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            await using var reg = cts.Token.Register(static s =>
            {
                try { ((Process)s!).Kill(entireProcessTree: true); } catch { /* already gone */ }
            }, Process);

            // Drain BOTH streams concurrently. Reading only stdout while stderr fills its
            // pipe buffer is a classic deadlock; git writes diagnostics to stderr.
            var stdout = Process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = Process.StandardError.ReadToEndAsync(cts.Token);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await Process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return stdout.Result;
        }

        public void Dispose() => Process.Dispose();
    }
}
