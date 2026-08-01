using System.Diagnostics;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacNetworkConnectionService(ILogger<MacNetworkConnectionService> logger) : INetworkConnectionService
{
    public async ValueTask<bool> EnsureConnectedAsync(string uncPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uncPath))
            return false;

        // Convert UNC path to SMB URL if needed
        var smbUrl = ConvertToSmbUrl(uncPath);
        if (smbUrl is null)
            return false;

        try
        {
            // Use macOS 'open' command to mount the share - this shows Finder will prompt for credentials
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = smbUrl,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // Give the system a moment to mount
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            // Check if mounted
            return IsMounted(smbUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to '{Path}'", uncPath);
            return false;
        }
    }

    private static string? ConvertToSmbUrl(string uncPath)
    {
        if (uncPath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            return uncPath;

        if (!uncPath.StartsWith(@"\\", StringComparison.Ordinal))
            return null;

        // Convert \\server\share to smb://server/share
        var parts = uncPath[2..].Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        return $"smb://{parts[0]}/{parts[1]}";
    }

    private static bool IsMounted(string smbUrl)
    {
        try
        {
            if (Directory.Exists("/Volumes"))
            {
                var shareName = smbUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (shareName is not null)
                {
                    return Directory.GetDirectories("/Volumes")
                        .Any(d => Path.GetFileName(d).Equals(shareName, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch { }
        return false;
    }
}