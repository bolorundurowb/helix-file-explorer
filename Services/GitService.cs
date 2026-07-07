using System.Diagnostics;
using System.IO;
using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>
/// Git status reader built on the git CLI. No native bindings, so it works against
/// any git on PATH and adds zero extra dependencies. Output parsing is allocation-light.
/// </summary>
public sealed class GitService : IGitService
{
    private const string GitExe = "git";

    public bool IsInsideRepository(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        string? dir = path;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    public async ValueTask<GitStatus> GetStatusAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return GitStatus.Empty;

        // fast-out: walk up looking for .git
        string? root = path;
        bool found = false;
        while (!string.IsNullOrEmpty(root))
        {
            if (Directory.Exists(Path.Combine(root, ".git"))) { found = true; break; }
            root = Path.GetDirectoryName(root);
        }
        if (!found || root is null) return GitStatus.Empty;

        try
        {
            string branch = await RunGitAsync(root, "branch --show-current", token).ConfigureAwait(false);
            string porcelain = await RunGitAsync(root, "status --porcelain", token).ConfigureAwait(false);
            string remote = await RunGitAsync(root, "remote", token).ConfigureAwait(false);

            int staged = 0, unstaged = 0, untracked = 0;
            if (porcelain.Length > 0)
            {
                int i = 0;
                int n = porcelain.Length;
                while (i < n)
                {
                    // Each line: XY <space> path. X is index, Y is work-tree.
                    char x = i < n ? porcelain[i] : ' ';
                    char y = (i + 1) < n ? porcelain[i + 1] : ' ';

                    if (x == '?' && y == '?') untracked++;
                    else if (x == '!' && y == '!') { } // ignored
                    else
                    {
                        if (x != ' ' && x != '?') staged++;
                        if (y != ' ' && y != '?') unstaged++;
                    }
                    int next = porcelain.IndexOf('\n', i);
                    i = next < 0 ? n : next + 1;
                }
            }

            return new GitStatus(branch.Trim(), staged, unstaged, untracked, remote.Length > 0);
        }
        catch (Exception ex) when (ex is OperationCanceledException or not IOException)
        {
            if (ex is OperationCanceledException) throw;
            Debug.WriteLine($"GitService.GetStatusAsync failed for '{path}': {ex.Message}");
            return GitStatus.Empty;
        }
    }

    private static async Task<string> RunGitAsync(string workingDir, string args, CancellationToken token)
    {
        using var psi = new ProcessRunInfo(
            new ProcessStartInfo(GitExe, args)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            });
        return await psi.RunAndGetOutputAsync(token).ConfigureAwait(false);
    }

    /// <summary>Sealed IDisposable wrapper around <see cref="Process"/> the compiler can stack-allocate via a using.</summary>
    private sealed class ProcessRunInfo : IDisposable
    {
        public Process Process { get; }
        public ProcessRunInfo(ProcessStartInfo si) { Process = new Process { StartInfo = si, EnableRaisingEvents = false }; }
        public Task<string> RunAndGetOutputAsync(CancellationToken token) => RunAsync(token);
        private async Task<string> RunAsync(CancellationToken token)
        {
            if (!Process.Start()) return string.Empty;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.Token.Register(static s =>
            {
                try { ((Process)s!).Kill(entireProcessTree: true); } catch { }
            }, Process);
            string output = await Process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await Process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return output;
        }
        public void Dispose() => Process.Dispose();
    }
}