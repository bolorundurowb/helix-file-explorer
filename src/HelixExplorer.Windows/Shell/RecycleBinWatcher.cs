using System.Security.Principal;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Watches the current user's per-drive <c>$RECYCLE.BIN\{SID}</c> folders and raises a single
/// changed event when items are added or removed. This avoids polling the Recycle Bin shell
/// namespace to detect changes.
/// </summary>
public sealed class RecycleBinWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly string _sid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Raised when a file is created, deleted, or renamed in any watched Recycle Bin folder.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Starts watching all accessible <c>$RECYCLE.BIN\{SID}</c> folders on local, ready drives.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed || string.IsNullOrEmpty(_sid))
            return;

        Stop();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType != DriveType.Network))
        {
            var path = Path.Combine(drive.RootDirectory.FullName, "$RECYCLE.BIN", _sid);
            if (!Directory.Exists(path))
                continue;

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

    /// <summary>
    /// Stops all active watchers.
    /// </summary>
    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Dispose();
        }

        _watchers.Clear();
        _started = false;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore metadata-only $I changes if you only care about visible items; for now we surface
        // all changes so the UI can decide whether to refresh.
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
