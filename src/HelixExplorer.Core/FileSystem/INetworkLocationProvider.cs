using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface INetworkLocationProvider
{
    /// <summary>
    /// Discovers top-level network locations (computers/servers). Returns a result carrying a status so
    /// callers can distinguish "nothing discovered" from "discovery failed".
    /// </summary>
    ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default);
}
