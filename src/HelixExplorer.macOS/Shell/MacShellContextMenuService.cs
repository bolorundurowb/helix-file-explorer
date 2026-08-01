using System.Diagnostics;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.Shell;

public sealed class MacShellContextMenuService(ILogger<MacShellContextMenuService> logger) : IShellContextMenuService
{
    public async ValueTask ShowMoreOptionsAsync(
        string folderPath,
        IReadOnlyList<string> paths,
        nint ownerHwnd,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (paths.Count > 0)
            {
                // Reveal in Finder (equivalent to "Show in Folder")
                var path = paths[0];
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await RunOsascriptAsync(
                        $"tell application \"Finder\" to reveal POSIX file \"{EscapePath(path)}\"",
                        cancellationToken).ConfigureAwait(false);
                    await RunOsascriptAsync(
                        "tell application \"Finder\" to activate",
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else if (!string.IsNullOrWhiteSpace(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = EscapeArg(folderPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Show in Finder failed");
        }
    }

    public async ValueTask ShowPropertiesAsync(string path, nint ownerHwnd, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunOsascriptAsync(
                $"tell application \"Finder\" to open information window of (POSIX file \"{EscapePath(path)}\" as alias)",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Show properties failed");
        }
    }

    private static async Task RunOsascriptAsync(string script, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e {EscapeArg(script)}",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
    }

    private static string EscapePath(string path)
        => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeArg(string arg)
        => $"\"{arg.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}