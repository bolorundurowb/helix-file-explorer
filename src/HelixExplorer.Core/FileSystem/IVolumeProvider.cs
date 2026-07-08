using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

public interface IVolumeProvider
{
    IReadOnlyList<VolumeInfo> GetVolumes();
}
