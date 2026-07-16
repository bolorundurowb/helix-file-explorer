using System.Diagnostics;
using Avalonia.Threading;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Callback surface used by <see cref="PaneRefreshCoordinator"/> to read pane state and
/// mutate UI-bound members. Implementations are expected to be thread-safe for reads that
/// occur before the async work begins; all mutation callbacks are invoked on the UI thread.
/// </summary>
public interface IPaneRefreshHost
{
    bool IsDisposed { get; }
    string CurrentPath { get; }
    bool IsHome { get; }
    bool IsArchive { get; }
    bool IsShellNamespace { get; }
    bool ShowHiddenFiles { get; }
    bool ShowFileExtensions { get; }
    bool IsFilterVisible { get; }
    string FilterText { get; }
    SortColumn SortColumn { get; }
    bool SortDescending { get; }
    DirectorySortMode DirectorySort { get; }
    bool IsGridView { get; }
    double ThumbnailSize { get; }
    LayoutMode ViewMode { get; }
    IReadOnlyList<EntryItemViewModel> Entries { get; }

    void SetLoading(bool loading);
    void SetStatusText(string text);
    ListingPublishResult ApplySortAndPublish(ListingPublishRequest request);
    void OnNavigated();
    void ApplyGitSnapshot(GitStatusSnapshot snapshot, GitStatus status);
    void StopWatcher();
    void RestartWatcher();
    void RequestRefresh();
}

/// <summary>
/// Owns the pane refresh lifecycle: cancellation, stale-result suppression, watcher coalescing,
/// Git status refresh, and entry visual requests. Keeps <see cref="PaneViewModel"/> focused on
/// state and commands rather than async orchestration.
/// </summary>
public sealed class PaneRefreshCoordinator(
    IFileSystemProvider fileSystem,
    IArchiveProvider archive,
    IGitProvider git,
    FileVisualService visuals,
    ILogger<PaneRefreshCoordinator> logger)
    : IDisposable
{
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _gitCts;
    private CancellationTokenSource? _visualCts;

    /// <summary>
    /// Bounded concurrency for entry visual loading. Conservative starting point; adjust here after
    /// measuring large photo folders (and consider a separate icon vs thumbnail cap).
    /// </summary>
    private const int MaxConcurrentVisuals = 4;

    private readonly BoundedVisualLoader _visualLoader = new(MaxConcurrentVisuals);
    private int _refreshGeneration;
    private bool _watcherRefreshPending;
    private bool _refreshInFlight;
    private bool _disposed;

    public bool IsRefreshInFlight => _refreshInFlight;

    public async Task RefreshAsync(IPaneRefreshHost host, bool showLoading)
    {
        if (host.IsDisposed)
            return;

        var path = host.CurrentPath;
        if (string.IsNullOrEmpty(path) || host.IsHome || host.IsDisposed)
            return;

        var generation = Interlocked.Increment(ref _refreshGeneration);
        CancelGitRefresh();

        var previous = _refreshCts;
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }

        var token = cts.Token;
        _refreshInFlight = true;
        host.StopWatcher();

        try
        {
            if (showLoading)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation == _refreshGeneration && !host.IsDisposed)
                        host.SetLoading(true);
                });
            }

            var sw = Stopwatch.StartNew();
            DirectoryListing listing;
            string? errorMessage = null;
            try
            {
                if (ArchivePath.IsVirtual(path))
                {
                    var entries = await archive.EnumerateAsync(path, token).ConfigureAwait(false);
                    listing = new DirectoryListing(path, entries);
                }
                else
                {
                    listing = await fileSystem.GetDirectoryContentsAsync(path, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                errorMessage = FileSystemError.Describe(ex, path);
                logger.LogError(ex, "Pane refresh failed for '{Path}'", path);
                listing = DirectoryListing.Empty;
            }

            sw.Stop();
            logger.LogDebug("Refresh '{Path}' listed {Count} items in {ElapsedMs} ms",
                path, listing.Count, sw.ElapsedMilliseconds);

            if (IsTokenCancelled(token) || host.IsDisposed || generation != _refreshGeneration)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsTokenCancelled(token) || host.IsDisposed || generation != _refreshGeneration)
                    return;

                var result = host.ApplySortAndPublish(new ListingPublishRequest
                {
                    AllEntries = listing.Entries,
                    GitSnapshot = GitStatusSnapshot.Empty,
                    ShowHiddenFiles = host.ShowHiddenFiles,
                    ShowFileExtensions = host.ShowFileExtensions,
                    IsFilterVisible = host.IsFilterVisible,
                    FilterText = host.FilterText,
                    SortColumn = host.SortColumn,
                    SortDescending = host.SortDescending,
                    DirectorySort = host.DirectorySort
                });

                if (!string.IsNullOrEmpty(errorMessage))
                    host.SetStatusText(errorMessage);

                host.OnNavigated();

                if (showLoading)
                    host.SetLoading(false);

                // Visuals are requested by ApplySortAndPublish on the host; do not double-queue here.
            });

            if (IsTokenCancelled(token) || host.IsDisposed || generation != _refreshGeneration)
                return;

            StartGitStatusRefresh(host, generation, path);
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                _refreshCts = null;
                _refreshInFlight = false;

                if (!host.IsDisposed)
                    host.RestartWatcher();

                if (_watcherRefreshPending && !host.IsDisposed)
                {
                    _watcherRefreshPending = false;
                    host.RequestRefresh();
                }
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Called when the file-system watcher reports a change. If a refresh is already in flight
    /// the notification is coalesced into a single follow-up refresh.
    /// </summary>
    public void NotifyContentChanged(IPaneRefreshHost host)
    {
        if (_disposed || host.IsDisposed)
            return;

        if (_refreshInFlight)
        {
            _watcherRefreshPending = true;
            return;
        }

        host.RequestRefresh();
    }

    public void RequestEntryVisuals(IPaneRefreshHost host, IReadOnlyList<EntryItemViewModel>? targets = null)
    {
        // Snapshot first so empty VisualTargets (sort/filter reuse) do not cancel in-flight loads.
        var entries = (targets ?? host.Entries).ToList();
        if (entries.Count == 0)
            return;

        CancelVisuals();
        _visualCts = new CancellationTokenSource();
        var ct = _visualCts.Token;
        var size = host.IsGridView ? (int)host.ThumbnailSize : 20;
        var isGrid = host.IsGridView;

        // Bound concurrency so opening a folder of photos does not spawn one decode task per entry.
        _ = _visualLoader.RunAsync(
            entries,
            (entry, token) => entry.RefreshVisualAsync(visuals, size, isGrid, token),
            ct);
    }

    public void CancelRefresh()
    {
        var previous = Interlocked.Exchange(ref _refreshCts, null);
        if (previous is null)
            return;

        // Cancel only; the owning RefreshAsync finally disposes the CTS.
        try { previous.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void CancelGitRefresh()
    {
        if (_gitCts is null)
            return;

        try { _gitCts.Cancel(); } catch (ObjectDisposedException) { }
        _gitCts.Dispose();
        _gitCts = null;
    }

    public void CancelVisuals()
    {
        try { _visualCts?.Cancel(); } catch (ObjectDisposedException) { }
        _visualCts?.Dispose();
        _visualCts = null;
    }

    private void StartGitStatusRefresh(IPaneRefreshHost host, int generation, string path)
    {
        if (!git.IsInsideRepository(path))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _refreshGeneration || host.IsDisposed)
                    return;
                if (!PathsEqual(path, host.CurrentPath))
                    return;

                host.ApplyGitSnapshot(GitStatusSnapshot.Empty, GitStatus.Empty);
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _gitCts = cts;
        _ = RefreshGitStatusAsync(host, generation, path, cts);
    }

    private async Task RefreshGitStatusAsync(IPaneRefreshHost host, int generation, string path, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var snapshot = await git.GetStatusAsync(path, token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsTokenCancelled(token) || host.IsDisposed || generation != _refreshGeneration)
                    return;
                if (!PathsEqual(path, host.CurrentPath))
                    return;

                host.ApplyGitSnapshot(snapshot, snapshot.Status);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git status refresh failed for '{Path}'", path);
        }
        finally
        {
            if (ReferenceEquals(_gitCts, cts))
            {
                _gitCts = null;
                cts.Dispose();
            }
        }
    }

    private static bool PathsEqual(string a, string b)
        => PathUtilities.PathsEqual(a, b);

    private static bool IsTokenCancelled(CancellationToken token)
    {
        try { return token.IsCancellationRequested; }
        catch (ObjectDisposedException) { return true; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        CancelRefresh();
        CancelGitRefresh();
        CancelVisuals();
    }
}
