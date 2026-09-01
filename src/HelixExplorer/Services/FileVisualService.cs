using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Services;

public sealed class FileVisualService(IFileVisualProvider provider) : IDisposable
{
    private const int MaxCacheEntries = 512;

    private readonly ConcurrentDictionary<VisualCacheKey, Task<Bitmap?>> _cache = new();
    private readonly ConcurrentDictionary<Bitmap, int> _refs = new();
    private readonly HashSet<Bitmap> _cacheOwners = [];
    private readonly LinkedList<VisualCacheKey> _lruOrder = new();
    private readonly Dictionary<VisualCacheKey, LinkedListNode<VisualCacheKey>> _lruNodes = new();
    private readonly object _lruLock = new();
    private readonly object _ownershipLock = new();
    private readonly SemaphoreSlim _uiDecodeGate = new(1, 1);
    private bool _disposed;

    public async Task<Bitmap?> GetBitmapAsync(
        string path,
        bool isDirectory,
        int size,
        bool preferThumbnail,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
            return null;

        var key = new VisualCacheKey(path, size, preferThumbnail);
        Touch(key);

        var task = _cache.GetOrAdd(key, static (k, state) =>
            state.self.LoadAndCacheAsync(k, state.isDirectory), (self: this, isDirectory));

        if (task.IsCanceled || task.IsFaulted)
        {
            _cache.TryRemove(key, out _);
            RemoveFromLru(key);
            task = _cache.GetOrAdd(key, static (k, state) =>
                state.self.LoadAndCacheAsync(k, state.isDirectory), (self: this, isDirectory));
            Touch(key);
        }

        while (true)
        {
            var bitmap = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (bitmap is null)
                return null;

            lock (_ownershipLock)
            {
                // Eviction and acquisition share this gate. A completed task removed from the cache
                // cannot be disposed between this check and taking the binding reference.
                if (_cache.TryGetValue(key, out var cached) && ReferenceEquals(cached, task))
                {
                    _refs.AddOrUpdate(bitmap, 1, static (_, n) => n + 1);
                    return bitmap;
                }
            }

            task = _cache.GetOrAdd(key, static (k, state) =>
                state.self.LoadAndCacheAsync(k, state.isDirectory), (self: this, isDirectory));
            Touch(key);
        }
    }

    /// <summary>
    /// Drop a live binding's hold. Disposes the bitmap only when no listing still displays it
    /// and it is no longer in the LRU cache.
    /// </summary>
    public void Release(Bitmap? bitmap)
    {
        if (bitmap is null)
            return;

        lock (_ownershipLock)
        {
            if (!_refs.TryGetValue(bitmap, out var count))
                return;

            if (count > 1)
            {
                _refs[bitmap] = count - 1;
                return;
            }

            _refs.TryRemove(bitmap, out _);
            TryDisposeUncached(bitmap);
        }
    }

    private async Task<Bitmap?> LoadAndCacheAsync(VisualCacheKey key, bool isDirectory)
    {
        if (_disposed)
            return null;

        try
        {
            var request = new FileVisualRequest(key.Path, isDirectory, key.Size, key.PreferThumbnail);
            var data = await provider.GetAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (data is null || data.Png.Length == 0)
            {
                _cache.TryRemove(key, out _);
                RemoveFromLru(key);
                return null;
            }

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

            lock (_ownershipLock)
                _cacheOwners.Add(bitmap);
            EvictIfNeeded();
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
        List<Bitmap>? doomed = null;
        lock (_lruLock)
        {
            while (_lruOrder.Count > MaxCacheEntries)
            {
                var oldest = _lruOrder.First!.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(oldest);

                if (_cache.TryRemove(oldest, out var task) && task.IsCompletedSuccessfully && task.Result is { } bitmap)
                    (doomed ??= []).Add(bitmap);
            }
        }

        if (doomed is null)
            return;

        lock (_ownershipLock)
        {
            foreach (var bitmap in doomed)
            {
                _cacheOwners.Remove(bitmap);
                TryDisposeUncached(bitmap);
            }
        }
    }

    private void TryDisposeUncached(Bitmap bitmap)
    {
        if (_refs.ContainsKey(bitmap) || _cacheOwners.Contains(bitmap))
            return;

        bitmap.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsCompletedSuccessfully && kvp.Value.Result is { } bitmap)
            {
                lock (_ownershipLock)
                {
                    _cacheOwners.Remove(bitmap);
                    TryDisposeUncached(bitmap);
                }
            }
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
