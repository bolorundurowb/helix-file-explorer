namespace HelixExplorer.Core.FileSystem;

public interface IFileChangeWatcher : IDisposable
{
    event EventHandler? Changed;

    void Watch(string path);

    void Stop();
}
