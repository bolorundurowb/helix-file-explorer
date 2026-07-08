using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface INetworkLocationProvider
{
    ValueTask<IReadOnlyList<NetworkLocationInfo>> GetNetworkLocationsAsync(CancellationToken cancellationToken = default);
}
