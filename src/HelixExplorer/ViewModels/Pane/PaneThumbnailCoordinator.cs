using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

public interface IPaneThumbnailHost
{
    bool IsDisposed { get; }
    bool IsGridView { get; }
    double ThumbnailSize { get; }
    IReadOnlyList<EntryItemViewModel> Entries { get; }
}

public sealed class PaneThumbnailCoordinator : IDisposable
{
    private const int MaxConcurrentVisuals = 4;

    private readonly FileVisualService? _visuals;
    private readonly ILogger<PaneThumbnailCoordinator> _logger;
    private readonly BoundedVisualLoader _visualLoader = new(MaxConcurrentVisuals);
    private CancellationTokenSource? _reloadCancellation;
    private CancellationTokenSource? _visualCancellation;
    private bool _disposed;

    public PaneThumbnailCoordinator(
        FileVisualService visuals,
        ILogger<PaneThumbnailCoordinator> logger)
    {
        _visuals = visuals;
        _logger = logger;
    }

    public PaneThumbnailCoordinator(ILogger<PaneThumbnailCoordinator> logger)
    {
        _logger = logger;
    }

    public double ApplySizeChange(PaneThumbnailSizeChange change)
    {
        var clamped = Math.Clamp(
            change.Value,
            PaneViewModel.MinThumbnailSize,
            PaneViewModel.MaxThumbnailSize);

        if (Math.Abs(clamped - change.Value) > double.Epsilon)
            return clamped;

        if (change.IsGridView)
            ScheduleReload(change.RequestEntryVisuals);

        change.OnLayoutChanged();
        change.PersistPreferences();
        return clamped;
    }

    public void ApplyViewModeChange(PaneThumbnailViewModeChange change)
    {
        if (change.IsGridView)
            ScheduleReload(change.RequestEntryVisuals);
        else
            change.RequestEntryVisuals();

        change.RebuildGridItems();
        change.NotifyGroupProperties();
        change.OnLayoutChanged();
        change.PersistPreferences();
    }

    public void RequestEntryVisuals(
        IPaneThumbnailHost host,
        IReadOnlyList<EntryItemViewModel>? targets = null)
    {
        if (_disposed || host.IsDisposed || _visuals is null)
            return;

        // Empty visual targets mean the publish reused existing entries; preserve their in-flight loads.
        var entries = (targets ?? host.Entries).ToList();
        if (entries.Count == 0)
            return;

        CancelVisuals();
        _visualCancellation = new CancellationTokenSource();
        var cancellationToken = _visualCancellation.Token;
        var isGrid = host.IsGridView;
        var size = isGrid ? (int)host.ThumbnailSize : 20;

        _ = _visualLoader.RunAsync(
            entries,
            (entry, token) => entry.RefreshVisualAsync(_visuals, size, isGrid, token),
            cancellationToken);
    }

    private void ScheduleReload(Action requestEntryVisuals)
    {
        if (_disposed)
            return;

        try { _reloadCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        _reloadCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _reloadCancellation = cts;
        FireAndForgetSafe.Run(ReloadAfterDelayAsync(cts, requestEntryVisuals), _logger);
    }

    private async Task ReloadAfterDelayAsync(
        CancellationTokenSource cts,
        Action requestEntryVisuals)
    {
        try
        {
            await Task.Delay(175, cts.Token).ConfigureAwait(true);
            if (_disposed || !ReferenceEquals(_reloadCancellation, cts))
                return;

            requestEntryVisuals();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(cts, Interlocked.CompareExchange(ref _reloadCancellation, null, cts)))
                cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try { _reloadCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        _reloadCancellation?.Dispose();
        _reloadCancellation = null;
        CancelVisuals();
    }

    private void CancelVisuals()
    {
        try { _visualCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        _visualCancellation?.Dispose();
        _visualCancellation = null;
    }
}

public sealed record PaneThumbnailSizeChange(
    double Value,
    bool IsGridView,
    Action RequestEntryVisuals,
    Action OnLayoutChanged,
    Action PersistPreferences);

public sealed record PaneThumbnailViewModeChange(
    bool IsGridView,
    Action RequestEntryVisuals,
    Action RebuildGridItems,
    Action NotifyGroupProperties,
    Action OnLayoutChanged,
    Action PersistPreferences);
