using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Open/close, pin, auto-dismiss, and unread-failure badge for the status centre card.
/// Progress itself lives on <see cref="FileOperationReporter"/> so the status-bar chip can bind it
/// even when the card is closed.
/// </summary>
public sealed partial class StatusCentreCoordinator : ObservableObject, IDisposable
{
    public static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(3);

    private readonly FileOperationReporter _reporter;
    private readonly Func<bool> _isCommandPaletteOpen;
    private readonly IStatusCentreScheduler _scheduler;
    private IDisposable? _autoDismiss;
    private bool _openedFromIdle;
    private bool _disposed;

    public StatusCentreCoordinator(
        FileOperationReporter reporter,
        Func<bool> isCommandPaletteOpen,
        IStatusCentreScheduler? scheduler = null)
    {
        _reporter = reporter;
        _isCommandPaletteOpen = isCommandPaletteOpen;
        _scheduler = scheduler ?? new DispatcherStatusCentreScheduler();
        _reporter.PropertyChanged += OnReporterPropertyChanged;
        _reporter.Completed.CollectionChanged += OnCompletedChanged;
    }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private bool _isPinned;

    [ObservableProperty] private bool _isHovered;

    [ObservableProperty] private bool _hasUnreadFailure;

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open(fromUser: true);
    }

    public void Close()
    {
        CancelAutoDismiss();
        IsOpen = false;
    }

    public void SetHovered(bool hovered) => IsHovered = hovered;

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    partial void OnIsPinnedChanged(bool value) => RefreshAutoDismiss();

    partial void OnIsHoveredChanged(bool value) => RefreshAutoDismiss();

    private void Open(bool fromUser)
    {
        _openedFromIdle = fromUser && !_reporter.HasActive;
        HasUnreadFailure = false;
        IsOpen = true;
    }

    private void OnReporterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (e.PropertyName != nameof(FileOperationReporter.HasActive))
            return;

        if (_reporter.HasActive)
            OnOperationBegan();
        else
            OnOperationEnded();
    }

    private void OnOperationBegan()
    {
        _openedFromIdle = false;
        CancelAutoDismiss();

        if (!_isCommandPaletteOpen())
            Open(fromUser: false);
    }

    private void OnOperationEnded()
    {
        var latest = _reporter.Completed.Count > 0 ? _reporter.Completed[0] : null;
        if (latest is { Failed: true } or { Cancelled: true })
        {
            CancelAutoDismiss();
            if (!IsOpen)
                HasUnreadFailure = true;
            return;
        }

        RefreshAutoDismiss();
    }

    private void OnCompletedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (e.Action == NotifyCollectionChangedAction.Reset)
            HasUnreadFailure = false;
    }

    private void RefreshAutoDismiss()
    {
        CancelAutoDismiss();
        if (!ShouldAutoDismiss())
            return;

        _autoDismiss = _scheduler.Schedule(AutoDismissDelay, () =>
        {
            if (_disposed || !ShouldAutoDismiss())
                return;

            Close();
        });
    }

    private bool ShouldAutoDismiss()
        => IsOpen
           && !IsPinned
           && !IsHovered
           && !_openedFromIdle
           && !_reporter.HasActive
           && _reporter.Completed.Count > 0
           && _reporter.Completed[0].Succeeded;

    private void CancelAutoDismiss()
    {
        _autoDismiss?.Dispose();
        _autoDismiss = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAutoDismiss();
        _reporter.PropertyChanged -= OnReporterPropertyChanged;
        _reporter.Completed.CollectionChanged -= OnCompletedChanged;
    }
}

public interface IStatusCentreScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

internal sealed class DispatcherStatusCentreScheduler : IStatusCentreScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        EventHandler handler = null!;
        handler = (_, _) =>
        {
            timer.Tick -= handler;
            timer.Stop();
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return new TimerStop(timer, handler);
    }

    private sealed class TimerStop(DispatcherTimer timer, EventHandler handler) : IDisposable
    {
        public void Dispose()
        {
            timer.Tick -= handler;
            timer.Stop();
        }
    }
}
