using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface INetworkLocationProvider
{
    /// <summary>
    /// Result status distinguishes "nothing discovered" from "discovery failed".
    /// </summary>
    ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default);
}
