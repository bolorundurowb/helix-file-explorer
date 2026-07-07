using System.IO;
using HelixExplorer.Infrastructure;

namespace HelixExplorer.Services;

/// <summary>
/// Debounced file-system change observer. One <see cref="FileSystemWatcher"/> per
/// monitored directory; change events coalesce via <see cref="AsyncDebouncer"/> so
/// the consumer only refreshes once after the burst settles.
/// </summary>
public sealed class FileChangeWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly AsyncDebouncer _debouncer = new(TimeSpan.FromMilliseconds(500));
    private Action? _pendingRefresh;
    private bool _disposed;

    /// <summary>Begin watching <paramref name="path"/>; <paramref name="onChanged"/> fires after the quiet period.</summary>
    public void Watch(string path, Action onChanged)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            Stop();
            return;
        }

        Stop();

        _pendingRefresh = onChanged;

        // Root virtual paths inside archives have no watcher; only real directories.
        if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += static (_, _) => { /* handled via shared debouncer trigger below */ };
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
            try
            {
                action();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileChangeWatcherService refresh threw: {ex.Message}");
            }
            return Task.CompletedTask;
        });
    }

    public void Stop()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch { }
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