using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// <see cref="IFileChangeWatcher"/> that never touches the OS; tests drive change notifications
/// explicitly via <see cref="Notify"/>.
/// </summary>
public sealed class VirtualFileChangeWatcher : IFileChangeWatcher
{
    public event EventHandler? Changed;

    public bool IsWatching { get; private set; }

    public void Watch(string path) => IsWatching = true;

    public void Stop() => IsWatching = false;

    public void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Stop();
}
