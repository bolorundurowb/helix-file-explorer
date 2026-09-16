using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class FileChangeWatcherService(ILogger<FileChangeWatcherService> logger) : IFileChangeWatcher
{
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(150);
    private readonly CancellationTokenScope _debounceScope = new();
    private FileSystemWatcher? _watcher;

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
            _watcher.Error += OnWatcherError;
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
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        if (ex is InternalBufferOverflowException)
            logger.LogWarning(ex, "FileChangeWatcher buffer overflow, requesting refresh");
        else
            logger.LogError(ex, "FileChangeWatcher error");

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception callbackEx)
        {
            logger.LogError(callbackEx, "Error handler callback error");
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        var token = _debounceScope.Renew();

        Task.Delay(_debounce, token).ContinueWith(t =>
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
        _debounceScope.Dispose();
    }
}
