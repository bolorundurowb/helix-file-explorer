using System.IO;
using HelixExplorer.Infrastructure;

namespace HelixExplorer.Services;

/// <summary>
/// Debounced file-system change observer. Each pane owns its own instance (do not share
/// one across panes — that would let a second pane's <see cref="Watch"/> tear down the
/// first pane's watcher). Change events — including <c>Created</c>, so completed
/// downloads and terminal file creation are picked up — coalesce via
/// <see cref="AsyncDebouncer"/> so the consumer refreshes once after the burst settles.
/// </summary>
public sealed class FileChangeWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly AsyncDebouncer _debouncer = new(TimeSpan.FromMilliseconds(400));
    private Action? _pendingRefresh;
    private bool _disposed;

    /// <summary>Begin watching <paramref name="path"/>; <paramref name="onChanged"/> fires after the quiet period.</summary>
    public void Watch(string path, Action onChanged)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();

        // Archive virtual paths and non-existent directories have nothing to watch.
        if (string.IsNullOrEmpty(path)
            || path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(path))
        {
            return;
        }

        _pendingRefresh = onChanged;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnEvent;
            _watcher.Changed += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += OnEvent;
            _watcher.Error += static (_, e) =>
                System.Diagnostics.Debug.WriteLine($"FileSystemWatcher error: {e.GetException().Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // Folders may not be watchable (network, protected, etc.). Continue without.
            System.Diagnostics.Debug.WriteLine($"FileChangeWatcherService.Watch: {ex.Message}");
            _watcher = null;
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        var action = _pendingRefresh;
        if (action is null) return;
        _debouncer.Schedule(_ =>
        {
            try { action(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileChangeWatcherService refresh threw: {ex.Message}");
            }
            return Task.CompletedTask;
        });
    }

    public void Stop()
    {
        _pendingRefresh = null;
        if (_watcher is null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnEvent;
            _watcher.Changed -= OnEvent;
            _watcher.Deleted -= OnEvent;
            _watcher.Renamed -= OnEvent;
            _watcher.Dispose();
        }
        catch { /* best effort */ }
        _watcher = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _debouncer.Dispose();
    }
}
