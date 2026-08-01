using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacNetworkLocationProvider(ILogger<MacNetworkLocationProvider> logger) : INetworkLocationProvider
{
    public async ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default)
    {
        var locations = new List<NetworkLocationInfo>();

        try
        {
            if (Directory.Exists("/Network"))
            {
                foreach (var entry in Directory.EnumerateDirectories("/Network"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(entry);
                    locations.Add(new NetworkLocationInfo(entry, name));
                }
            }

            if (Directory.Exists("/Volumes"))
            {
                foreach (var entry in Directory.EnumerateDirectories("/Volumes"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(entry);
                    if (IsSystemVolume(name))
                        continue;
                    locations.Add(new NetworkLocationInfo(entry, name));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Network enumeration failed");
        }

        return NetworkDiscoveryResult.From(locations);
    }

    private static bool IsSystemVolume(string name)
        => name is "Macintosh HD" or "System" or "Data" or "Preboot" or "Recovery" or "VM" or ".timemachine" or ".dmg";
}