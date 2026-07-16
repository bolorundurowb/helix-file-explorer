using System.Diagnostics;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Win32;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Launches a terminal at a directory. Prefers Windows Terminal when it is the user's default
/// (via the <c>DelegationTerminal</c> registry key) so <c>wt</c> opens a new tab in an existing
/// window with the chosen profile; otherwise falls back to pwsh, powershell, git-bash, or cmd.
/// </summary>
public sealed class WinTerminalLauncher : ITerminalLauncher
{
    // DelegationTerminal CLSIDs registered by Windows Terminal (stable + preview).
    // Source: microsoft/terminal policies/WindowsTerminal.admx.
    private static readonly string[] WindowsTerminalClsids =
    [
        "{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}", // Windows Terminal (stable)
        "{86633F1F-6454-40EC-89CE-DA4EBA977EE2}", // Windows Terminal Preview
    ];

    public bool TryOpenInDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        var fullPath = Path.GetFullPath(directoryPath);

        if (IsWindowsTerminalDefault() && TryOpenWindowsTerminalNewTab(fullPath))
            return true;

        return TryOpenFallbackShell(fullPath);
    }

    private static bool TryOpenWindowsTerminalNewTab(string directoryPath)
    {
        // "wt.exe" with UseShellExecute resolves via PATH / AppExecutionAlias without locating the binary.
        var args = $"-w 0 new-tab -d \"{directoryPath}\"";

        return TryStart(new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        });
    }

    private static bool IsWindowsTerminalDefault()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Console\%%Startup");
            if (key?.GetValue("DelegationTerminal") is string clsid)
                return WindowsTerminalClsids.Contains(clsid, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
        }

        return false;
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
