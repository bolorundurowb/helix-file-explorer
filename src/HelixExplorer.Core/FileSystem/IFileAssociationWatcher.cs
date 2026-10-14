namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Raises when Windows broadcasts a file-type association change so icon caches can drop
/// extension-shared entries without a process restart.
/// </summary>
public interface IFileAssociationWatcher : IDisposable
{
    event EventHandler? AssociationsChanged;

    void Start();
}
