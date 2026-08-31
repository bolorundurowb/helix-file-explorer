namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Watches per-drive <c>$RECYCLE.BIN\{SID}</c> so Recycle Bin changes are detected without
/// polling the shell namespace.
/// </summary>
public sealed class RecycleBinWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _started;
    private bool _disposed;

    public event EventHandler? Changed;

    public void Start()
    {
        if (_started || _disposed)
            return;

        Stop();

        foreach (var path in RecycleBinPaths.EnumerateUserBinFolders())
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Error += OnWatcherError;
                _watchers.Add(watcher);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        _started = true;
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }

        _watchers.Clear();
        _started = false;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // $I files are metadata-only; we still raise so the UI can decide whether to refresh.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow (and other watcher failures) drop change notifications; force a refresh.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}
