using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Owns recursive search debounce/cancellation for a pane. Filter (current-folder) stays on the listing path.
/// </summary>
public sealed class PaneSearchCoordinator(ILogger<PaneSearchCoordinator> logger) : IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    public bool ResultsCapped { get; private set; }

    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _searchCts, null);
        if (cts is null)
            return;

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
        ResultsCapped = false;
    }

    public void StartSearch(
        IFileSystemProvider fileSystem,
        string path,
        string query,
        SearchOptions options,
        Action<IReadOnlyList<FileSystemEntry>, bool> onResults,
        Func<bool> isAlive)
    {
        Cancel();
        if (_disposed || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(path))
            return;

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        FireAndForgetSafe.Run(RunAsync(fileSystem, path, query.Trim(), options, onResults, isAlive, cts), logger);
    }

    private async Task RunAsync(
        IFileSystemProvider fileSystem,
        string path,
        string query,
        SearchOptions options,
        Action<IReadOnlyList<FileSystemEntry>, bool> onResults,
        Func<bool> isAlive,
        CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            await Task.Delay(SearchDebounce, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !isAlive())
                return;

            var result = await fileSystem.SearchRecursiveAsync(path, query, options, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !isAlive())
                return;

            ResultsCapped = result.Capped;
            onResults(result.Entries, result.Capped);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(cts, Interlocked.CompareExchange(ref _searchCts, null, cts)))
                cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Cancel();
    }
}
