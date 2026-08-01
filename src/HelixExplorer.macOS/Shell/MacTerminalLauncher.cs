using System.Diagnostics;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.Shell;

public sealed class MacTerminalLauncher(ILogger<MacTerminalLauncher> logger) : ITerminalLauncher
{
    public bool TryOpenInDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        try
        {
            // Try iTerm2 first (common developer terminal)
            if (TryOpenWithBundle("com.googlecode.iterm2", directoryPath))
                return true;

            // Fall back to default Terminal.app
            if (TryOpenWithApp("Terminal.app", directoryPath))
                return true;

            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to launch terminal at '{Path}'", directoryPath);
            return false;
        }
    }

    private static bool TryOpenWithBundle(string bundleId, string directoryPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"-b {bundleId} \"{directoryPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit(3000);
        return process.ExitCode == 0;
    }

    private static bool TryOpenWithApp(string appName, string directoryPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"-a {appName} \"{directoryPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit(3000);
        return process.ExitCode == 0;
    }
}