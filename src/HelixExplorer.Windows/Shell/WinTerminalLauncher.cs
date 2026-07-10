using System.Diagnostics;
using System.Text.Json;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Windows.Shell;

public sealed class WinTerminalLauncher : ITerminalLauncher
{
    public bool TryOpenInDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        var fullPath = Path.GetFullPath(directoryPath);
        var defaultProfile = WindowsTerminalSettingsReader.TryGetDefaultProfile();
        var wtPath = ResolveWindowsTerminalPath();

        if (wtPath is not null)
        {
            if (TryOpenWindowsTerminal(fullPath, wtPath, defaultProfile?.Name))
                return true;

            if (TryOpenWindowsTerminal(fullPath, wtPath, profileName: null))
                return true;

            return TryOpenFallbackShell(fullPath, includeGitBash: false);
        }

        if (defaultProfile is not null && TryLaunchProfile(defaultProfile, fullPath))
            return true;

        return TryOpenFallbackShell(fullPath, includeGitBash: true);
    }

    private static bool TryOpenWindowsTerminal(string directoryPath, string wtPath, string? profileName)
    {
        var args = string.IsNullOrWhiteSpace(profileName)
            ? $"-d \"{directoryPath}\""
            : $"-d \"{directoryPath}\" -p \"{profileName}\"";

        return TryStart(new ProcessStartInfo
        {
            FileName = wtPath,
            Arguments = args,
            UseShellExecute = true
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

    private static bool TryLaunchProfile(TerminalProfile profile, string directoryPath)
    {
        var commandLine = ExpandEnvironment(profile.CommandLine);
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        if (IsGitShell(commandLine))
            return TryOpenGitBash(directoryPath);

        if (!TrySplitCommandLine(commandLine, out var executable, out var arguments))
            return false;

        executable = ExpandEnvironment(executable);
        if (!Path.IsPathRooted(executable))
            executable = ResolveExecutableOnPath(executable) ?? executable;

        if (executable.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return TryStart(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directoryPath,
                UseShellExecute = true
            });
        }

        if (executable.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || executable.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            var escapedPath = directoryPath.Replace("'", "''");
            return TryStart(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{escapedPath}'\"",
                UseShellExecute = true
            });
        }

        return TryStart(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = directoryPath,
            UseShellExecute = true
        });
    }

    private static bool TryOpenFallbackShell(string directoryPath, bool includeGitBash = true)
    {
        if (includeGitBash && TryOpenGitBash(directoryPath))
            return true;

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

    private static bool IsGitShell(string commandLine) =>
        commandLine.Contains("git-bash", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains(@"Git\bin\bash.exe", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains(@"Git\\bin\\bash.exe", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains("bash.exe", StringComparison.OrdinalIgnoreCase);

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

    private static bool TrySplitCommandLine(string commandLine, out string executable, out string arguments)
    {
        commandLine = commandLine.Trim();
        if (commandLine.Length == 0)
        {
            executable = string.Empty;
            arguments = string.Empty;
            return false;
        }

        if (commandLine[0] == '"')
        {
            var endQuote = commandLine.IndexOf('"', 1);
            if (endQuote < 0)
            {
                executable = commandLine.Trim('"');
                arguments = string.Empty;
                return true;
            }

            executable = commandLine[1..endQuote];
            arguments = commandLine[(endQuote + 1)..].TrimStart();
            return true;
        }

        var splitIndex = commandLine.IndexOf(' ');
        if (splitIndex < 0)
        {
            executable = commandLine;
            arguments = string.Empty;
            return true;
        }

        executable = commandLine[..splitIndex];
        arguments = commandLine[(splitIndex + 1)..].TrimStart();
        return true;
    }

    private static string ExpandEnvironment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return Environment.ExpandEnvironmentVariables(value.Replace('/', '\\'));
    }

    private static bool TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record TerminalProfile(string CommandLine, string? Name);

    private static class WindowsTerminalSettingsReader
    {
        public static TerminalProfile? TryGetDefaultProfile()
        {
            foreach (var path in GetSettingsPaths())
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    var profile = ParseDefaultProfile(File.ReadAllText(path));
                    if (profile is not null)
                        return profile;
                }
                catch
                {
                }
            }

            return null;
        }

        private static IEnumerable<string> GetSettingsPaths()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json");
            yield return Path.Combine(localAppData, @"Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json");
            yield return Path.Combine(localAppData, @"Microsoft\Windows Terminal\settings.json");
        }

        private static TerminalProfile? ParseDefaultProfile(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("defaultProfile", out var defaultProfileElement))
                return null;

            var defaultProfile = defaultProfileElement.GetString();
            if (string.IsNullOrWhiteSpace(defaultProfile))
                return null;

            if (!root.TryGetProperty("profiles", out var profilesElement))
                return null;

            if (profilesElement.TryGetProperty("list", out var listElement)
                && listElement.ValueKind == JsonValueKind.Array)
            {
                var profile = FindProfile(listElement, defaultProfile);
                if (profile is not null)
                    return profile;
            }

            return null;
        }

        private static TerminalProfile? FindProfile(JsonElement profiles, string defaultProfile)
        {
            var normalizedDefault = NormalizeProfileKey(defaultProfile);

            foreach (var profile in profiles.EnumerateArray())
            {
                if (!profile.TryGetProperty("commandline", out var commandLineElement))
                    continue;

                var commandLine = commandLineElement.GetString();
                if (string.IsNullOrWhiteSpace(commandLine))
                    continue;

                var name = profile.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var guid = profile.TryGetProperty("guid", out var guidElement)
                    ? guidElement.GetString()
                    : null;

                if (NormalizeProfileKey(guid) == normalizedDefault
                    || string.Equals(name, defaultProfile, StringComparison.OrdinalIgnoreCase))
                {
                    return new TerminalProfile(commandLine, name);
                }
            }

            return null;
        }

        private static string NormalizeProfileKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().Trim('{', '}').ToUpperInvariant();
        }
    }
}
