using System.Diagnostics;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Launches Windows Terminal (preferred) at a directory, opening a new tab in an existing window
/// so it matches the "Open in Terminal" behaviour users expect. Windows Terminal is invoked with
/// <c>wt -w 0 new-tab -d "&lt;dir&gt;"</c> (no <c>-p</c>) so the user's selected default profile is
/// used. Git Bash / pwsh / cmd remain only as final fallbacks when Windows Terminal cannot be found
/// or fails to launch.
/// </summary>
public sealed class WinTerminalLauncher : ITerminalLauncher
{
    public bool TryOpenInDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        var fullPath = Path.GetFullPath(directoryPath);
        var wtPath = ResolveWindowsTerminalPath();

        if (wtPath is not null)
        {
            if (TryOpenWindowsTerminalNewTab(fullPath, wtPath))
                return true;

            // The first attempt may fail on the app-execution-alias reparse point if the OS
            // proxy fails to resolve it; try the resolved target of the alias next.
            var resolved = TryResolveAliasTarget(wtPath);
            if (!string.IsNullOrEmpty(resolved)
                && !string.Equals(resolved, wtPath, StringComparison.OrdinalIgnoreCase)
                && TryOpenWindowsTerminalNewTab(fullPath, resolved))
            {
                return true;
            }
        }

        // Windows Terminal is genuinely unavailable — fall back to a registered shell. Only use
        // Git Bash as a *last* resort; we do not want to override the user's Windows Terminal
        // default profile (which may legitimately be Git Bash but should still open in WT).
        return TryOpenFallbackShell(fullPath);
    }

    private static bool TryOpenWindowsTerminalNewTab(string directoryPath, string wtPath)
    {
        // -w 0 targets the most recently used window; "new-tab" opens a tab inside it without
        // spawning a second window. Omitting -p lets Windows Terminal use the default profile
        // the user has set in settings.json.
        var args = $"-w 0 new-tab -d \"{directoryPath}\"";

        return TryStart(new ProcessStartInfo
        {
            FileName = wtPath,
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        });
    }

    private static string? ResolveWindowsTerminalPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"),
            Path.Combine(localAppData, @"Microsoft\WindowsApps\WindowsTerminal.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return ResolveExecutableOnPath("wt.exe");
    }

    /// <summary>
    /// The <c>wt.exe</c> under <c>Microsoft\WindowsApps</c> is a reparse point / execution alias.
    /// If launching it directly fails, resolve the underlying <c>WindowTerminal.exe</c> in the
    /// MSIX package install location and try again.
    /// </summary>
    private static string? TryResolveAliasTarget(string wtPath)
    {
        try
        {
            // Resolve any reparse points (true) — this returns the real wt.exe/WindowsTerminal.exe.
            var resolved = Path.GetFullPath(wtPath);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                return resolved;
        }
        catch
        {
        }

        // Last-resort: probe Program Files\WindowsApps for Microsoft.WindowsTerminal*\WindowsTerminal.exe
        try
        {
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");

            if (Directory.Exists(windowsApps))
            {
                foreach (var dir in Directory.EnumerateDirectories(windowsApps, "Microsoft.WindowsTerminal*"))
                {
                    var exe = Path.Combine(dir, "WindowsTerminal.exe");
                    if (File.Exists(exe))
                        return exe;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryOpenFallbackShell(string directoryPath)
    {
        var pwsh = ResolveExecutableOnPath("pwsh.exe");
        if (pwsh is not null
            && TryStart(new ProcessStartInfo
            {
                FileName = pwsh,
                Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{directoryPath.Replace("'", "''")}'\"",
                UseShellExecute = true
            }))
        {
            return true;
        }

        var powershell = ResolveExecutableOnPath("powershell.exe");
        if (powershell is not null
            && TryStart(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{directoryPath.Replace("'", "''")}'\"",
                UseShellExecute = true
            }))
        {
            return true;
        }

        if (TryOpenGitBash(directoryPath))
            return true;

        var cmd = ResolveExecutableOnPath("cmd.exe") ?? "cmd.exe";
        return TryStart(new ProcessStartInfo
        {
            FileName = cmd,
            WorkingDirectory = directoryPath,
            UseShellExecute = true
        });
    }

    private static bool TryOpenGitBash(string directoryPath)
    {
        var gitBash = ResolveGitBashPath();
        if (gitBash is null)
            return false;

        return TryStart(new ProcessStartInfo
        {
            FileName = gitBash,
            Arguments = $"--cd=\"{directoryPath}\"",
            UseShellExecute = true
        });
    }

    private static string? ResolveGitBashPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programFiles, @"Git\git-bash.exe"),
            Path.Combine(programFilesX86, @"Git\git-bash.exe"),
            Path.Combine(localAppData, @"Programs\Git\git-bash.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ResolveExecutableOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var segment in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(segment, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}