using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class FileChangeWatcherService(ILogger<FileChangeWatcherService> logger) : IFileChangeWatcher
{
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(150);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;

    public event EventHandler? Changed;

    public void Watch(string path)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Watch failed for '{Path}'", path);
        }
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Deleted -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileSystemEvent;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        Task.Delay(_debounce, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Debounce callback error");
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        Stop();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
