using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Services;

public sealed class FileVisualService
{
    private readonly IFileVisualProvider _provider;
    private readonly ConcurrentDictionary<VisualCacheKey, Bitmap> _cache = new();

    public FileVisualService(IFileVisualProvider provider)
    {
        _provider = provider;
    }

    public async Task<Bitmap?> GetBitmapAsync(
        string path,
        bool isDirectory,
        int size,
        bool preferThumbnail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var key = new VisualCacheKey(path, size, preferThumbnail);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var request = new FileVisualRequest(path, isDirectory, size, preferThumbnail);
        var data = await _provider.GetAsync(request, cancellationToken).ConfigureAwait(false);
        if (data is null || data.Png.Length == 0)
            return null;

        var bitmap = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var stream = new MemoryStream(data.Png);
            return new Bitmap(stream);
        });

        _cache[key] = bitmap;
        return bitmap;
    }

    private readonly record struct VisualCacheKey(string Path, int Size, bool PreferThumbnail);
}
