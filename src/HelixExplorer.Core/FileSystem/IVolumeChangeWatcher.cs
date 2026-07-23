namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Notifies when removable/local volumes are added or removed so drive lists can refresh live.
/// </summary>
public interface IVolumeChangeWatcher : IDisposable
{
    event EventHandler? VolumesChanged;

    void Start();
}
