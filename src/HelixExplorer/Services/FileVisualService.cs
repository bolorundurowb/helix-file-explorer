using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Services;

public sealed class FileVisualService : IDisposable
{
    private const int MaxCacheEntries = 512;

    private readonly IFileVisualProvider _provider;
    private readonly IFileAssociationWatcher _associations;
    private readonly ConcurrentDictionary<VisualCacheKey, Task<Bitmap?>> _cache = new();
    private readonly LinkedList<VisualCacheKey> _lruOrder = new();
    private readonly Dictionary<VisualCacheKey, LinkedListNode<VisualCacheKey>> _lruNodes = new();
    private readonly object _lruLock = new();
    private readonly SemaphoreSlim _uiDecodeGate = new(1, 1);
    private int _associationEpoch;
    private bool _disposed;

    public event EventHandler? AssociationsChanged;

    public FileVisualService(IFileVisualProvider provider, IFileAssociationWatcher associations)
    {
        _provider = provider;
        _associations = associations;
        _associations.AssociationsChanged += OnShellAssociationsChanged;
        _associations.Start();
    }

    public async Task<Bitmap?> GetBitmapAsync(
        string path,
        bool isDirectory,
        int size,
        bool preferThumbnail,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
            return null;

        var key = new VisualCacheKey(CachePathFor(path, isDirectory, preferThumbnail), size, preferThumbnail);
        Touch(key);

        // Loads must not be keyed to the caller's token; a cancelled caller would poison the cache.
        var epoch = Volatile.Read(ref _associationEpoch);
        var task = _cache.GetOrAdd(key, static (k, state) =>
            state.self.LoadAndCacheAsync(k, state.path, state.isDirectory, state.epoch),
            (self: this, path, isDirectory, epoch));

        if (task.IsCanceled || task.IsFaulted)
        {
            _cache.TryRemove(key, out _);
            RemoveFromLru(key);
            epoch = Volatile.Read(ref _associationEpoch);
            task = _cache.GetOrAdd(key, static (k, state) =>
                state.self.LoadAndCacheAsync(k, state.path, state.isDirectory, state.epoch),
                (self: this, path, isDirectory, epoch));
            Touch(key);
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string CachePathFor(string path, bool isDirectory, bool preferThumbnail)
    {
        if (isDirectory || preferThumbnail || FileVisualRules.HasPerFileIcon(path))
            return path;

        // Generic file icons are identical per extension; keying by extension lets a folder full of
        // the same file type share one shell query and PNG decode instead of thrashing the LRU cache.
        return Path.GetExtension(path).ToUpperInvariant();
    }

    private async Task<Bitmap?> LoadAndCacheAsync(
        VisualCacheKey key,
        string requestPath,
        bool isDirectory,
        int epoch)
    {
        if (_disposed)
            return null;

        try
        {
            var request = new FileVisualRequest(requestPath, isDirectory, key.Size, key.PreferThumbnail);
            var data = await _provider.GetAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (data is null || data.Png.Length == 0)
            {
                _cache.TryRemove(key, out _);
                RemoveFromLru(key);
                return null;
            }

            // Serialize UI-thread Bitmap construction so bounded loaders do not stampede the dispatcher.
            await _uiDecodeGate.WaitAsync().ConfigureAwait(false);
            Bitmap? bitmap;
            try
            {
                if (_disposed)
                    return null;

                bitmap = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var stream = new MemoryStream(data.Png);
                    return new Bitmap(stream);
                });
            }
            finally
            {
                _uiDecodeGate.Release();
            }

            EvictIfNeeded();
            DropIfAssociationChanged(key, epoch);
            return bitmap;
        }
        catch (Exception)
        {
            _cache.TryRemove(key, out _);
            RemoveFromLru(key);
            throw;
        }
    }

    private void Touch(VisualCacheKey key)
    {
        lock (_lruLock)
        {
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lruOrder.Remove(node);
                _lruOrder.AddLast(node);
            }
            else
            {
                var created = _lruOrder.AddLast(key);
                _lruNodes[key] = created;
            }
        }
    }

    private void RemoveFromLru(VisualCacheKey key)
    {
        lock (_lruLock)
        {
            if (_lruNodes.Remove(key, out var node))
                _lruOrder.Remove(node);
        }
    }

    private void EvictIfNeeded()
    {
        lock (_lruLock)
        {
            while (_lruOrder.Count > MaxCacheEntries)
            {
                var oldest = _lruOrder.First!.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(oldest);

                _cache.TryRemove(oldest, out _);
            }
        }
    }

    private void OnShellAssociationsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        InvalidateExtensionCache();
        if (Dispatcher.UIThread.CheckAccess())
            AssociationsChanged?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed)
                    AssociationsChanged?.Invoke(this, EventArgs.Empty);
            });
    }

    private void InvalidateExtensionCache()
    {
        Interlocked.Increment(ref _associationEpoch);

        VisualCacheKey[] keys;
        lock (_lruLock)
            keys = _lruOrder.Where(key => FileVisualRules.IsExtensionCacheKey(key.Path)).ToArray();

        foreach (var key in keys)
        {
            _cache.TryRemove(key, out _);
            RemoveFromLru(key);
        }
    }

    private void DropIfAssociationChanged(VisualCacheKey key, int epoch)
    {
        if (epoch == Volatile.Read(ref _associationEpoch) || !FileVisualRules.IsExtensionCacheKey(key.Path))
            return;

        _cache.TryRemove(key, out _);
        RemoveFromLru(key);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _associations.AssociationsChanged -= OnShellAssociationsChanged;

        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsCompletedSuccessfully)
                kvp.Value.Result?.Dispose();
        }

        _cache.Clear();

        lock (_lruLock)
        {
            _lruOrder.Clear();
            _lruNodes.Clear();
        }

        _uiDecodeGate.Dispose();
    }

    private readonly record struct VisualCacheKey(string Path, int Size, bool PreferThumbnail);
}
