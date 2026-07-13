using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Services;

public sealed class FileVisualService(IFileVisualProvider provider) : IDisposable
{
    private const int MaxCacheEntries = 512;

    private readonly ConcurrentDictionary<VisualCacheKey, Task<Bitmap?>> _cache = new();
    private readonly LinkedList<VisualCacheKey> _lruOrder = new();
    private readonly Dictionary<VisualCacheKey, LinkedListNode<VisualCacheKey>> _lruNodes = new();
    private readonly object _lruLock = new();
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

        // Loads must not be keyed to the caller's token; a cancelled caller would poison the cache.
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

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            var bitmap = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(data.Png);
                return new Bitmap(stream);
            });

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
        List<Task<Bitmap?>>? toDispose = null;

        lock (_lruLock)
        {
            while (_lruOrder.Count > MaxCacheEntries)
            {
                var oldest = _lruOrder.First!.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(oldest);

                if (_cache.TryRemove(oldest, out var task))
                    (toDispose ??= []).Add(task);
            }
        }

        if (toDispose is null)
            return;

        foreach (var task in toDispose)
        {
            if (!task.IsCompletedSuccessfully)
                continue;

            var bitmap = task.Result;
            if (bitmap is not null)
                Dispatcher.UIThread.Post(bitmap.Dispose);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

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
    }

    private readonly record struct VisualCacheKey(string Path, int Size, bool PreferThumbnail);
}
