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
    GroupByMode GroupBy { get; }
    void SetLoading(bool loading);
    void SetStatusText(string text);

    /// <summary>True while this generation's refresh has not finished. Esc and Stop use it.</summary>
    void SetRefreshActive(bool active);

    /// <summary>
    /// Stores the listing fault for the content panel. Null clears it. Applied on the next publish.
    /// </summary>
    void SetRefreshFault(string? message, bool accessDenied);
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
    ILogger<PaneRefreshCoordinator> logger)
    : IDisposable
{
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _gitCts;
    private int _refreshGeneration;
    private bool _watcherRefreshPending;
    private bool _refreshInFlight;
    private bool _disposed;

    /// <summary>
    /// How long a refresh runs before the pane shows the loading bar. Fast listings never flash it.
    /// Tests shorten this; production stays at 300ms.
    /// </summary>
    internal TimeSpan LoadingIndicatorDelay { get; set; } = TimeSpan.FromMilliseconds(300);

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
        var loadingCleared = !showLoading;
        var accessDenied = false;
        LoadingSignal? loadingSignal = showLoading ? new LoadingSignal(token) : null;
        var loadingTask = loadingSignal is null
            ? Task.CompletedTask
            : ShowLoadingIfStillRunningAsync(host, generation, loadingSignal);
        host.StopWatcher();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _refreshGeneration && !host.IsDisposed)
                    host.SetRefreshActive(true);
            });

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
                // A newer generation owns loading state; do not leave a cancelled refresh stuck.
                return;
            }
            catch (Exception ex)
            {
                errorMessage = FileSystemErrorMessages.Describe(ex, path);
                accessDenied = FileSystemErrorClassifier.FromException(ex, path) == FileSystemError.AccessDenied;
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
                // Path must still match: generation alone is not enough if cancel/generation and path diverge.
                if (!PathsEqual(path, host.CurrentPath))
                    return;

                host.SetRefreshFault(
                    string.IsNullOrEmpty(errorMessage) ? null : errorMessage,
                    accessDenied);

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
                    DirectorySort = host.DirectorySort,
                    GroupBy = host.GroupBy,
                    GroupingUtcNow = DateTime.UtcNow
                });

                if (!string.IsNullOrEmpty(errorMessage))
                    host.SetStatusText(errorMessage);

                host.OnNavigated();

                // Cancel the delay before it can raise the bar, then drop the bar if it already did.
                loadingSignal?.Suppress();
                if (showLoading)
                {
                    if (loadingSignal is { WasShown: true })
                        host.SetLoading(false);
                    loadingCleared = true;
                }

                // Visuals are requested by ApplySortAndPublish on the host; do not double-queue here.
            });

            if (IsTokenCancelled(token) || host.IsDisposed || generation != _refreshGeneration)
                return;
            if (!PathsEqual(path, host.CurrentPath))
                return;

            StartGitStatusRefresh(host, generation, path);
        }
        finally
        {
            // Stop the delay from firing after this generation has already decided. Cancelling makes
            // the wait return immediately, so a fast listing does not sit out the 300ms.
            loadingSignal?.Suppress();
            try
            {
                await loadingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (generation == _refreshGeneration)
            {
                _refreshCts = null;
                _refreshInFlight = false;

                var shown = loadingSignal is { WasShown: true };
                // Cancel and path-mismatch exits skip the publish; with no newer refresh to take over
                // the overlay, it would otherwise stay up.
                if (shown && !loadingCleared && !host.IsDisposed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (generation == _refreshGeneration && !host.IsDisposed)
                            host.SetLoading(false);
                    });
                }

                if (!host.IsDisposed)
                {
                    host.RestartWatcher();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (generation == _refreshGeneration && !host.IsDisposed)
                            host.SetRefreshActive(false);
                    });
                }

                if (_watcherRefreshPending && !host.IsDisposed)
                {
                    _watcherRefreshPending = false;
                    host.RequestRefresh();
                }
            }

            loadingSignal?.Dispose();
            cts.Dispose();
        }
    }

    private async Task ShowLoadingIfStillRunningAsync(IPaneRefreshHost host, int generation, LoadingSignal signal)
    {
        try
        {
            await Task.Delay(LoadingIndicatorDelay, signal.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (signal.IsSuppressed || host.IsDisposed || generation != _refreshGeneration)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (signal.IsSuppressed || host.IsDisposed || generation != _refreshGeneration)
                return;

            host.SetLoading(true);
            signal.MarkShown();
        });
    }

    /// <summary>
    /// Gates the delayed loading bar. Suppress cancels the wait so a finished refresh cannot flash it.
    /// </summary>
    private sealed class LoadingSignal : IDisposable
    {
        private readonly CancellationTokenSource _delayCts;
        private int _shown;

        public LoadingSignal(CancellationToken refreshToken)
        {
            _delayCts = CancellationTokenSource.CreateLinkedTokenSource(refreshToken);
        }

        public CancellationToken Token => _delayCts.Token;

        public bool IsSuppressed
        {
            get
            {
                try { return _delayCts.IsCancellationRequested; }
                catch (ObjectDisposedException) { return true; }
            }
        }

        public bool WasShown => Volatile.Read(ref _shown) != 0;

        public void MarkShown() => Volatile.Write(ref _shown, 1);

        public void Suppress()
        {
            try { _delayCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose() => _delayCts.Dispose();
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
    }
}
