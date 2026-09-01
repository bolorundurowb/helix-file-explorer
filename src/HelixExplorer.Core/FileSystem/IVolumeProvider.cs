using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IVolumeProvider
{
    IReadOnlyList<VolumeInfo> GetVolumes();

    ValueTask<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(GetVolumes());
}
