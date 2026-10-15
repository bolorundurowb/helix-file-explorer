using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Localization;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Open/close, pin, auto-dismiss, and unread-failure badge for the status centre card, plus the
/// transient note beside the chip and the taskbar signals that mirror the card.
/// Progress itself lives on <see cref="FileOperationReporter"/> so the status-bar chip can bind it
/// even when the card is closed.
/// </summary>
public sealed partial class StatusCentreCoordinator : ObservableObject, IDisposable
{
    public static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan NoteDuration = TimeSpan.FromSeconds(2);

    private readonly FileOperationReporter _reporter;
    private readonly Func<bool> _isCommandPaletteOpen;
    private readonly IStatusCentreScheduler _scheduler;
    private readonly IAttentionService? _attention;
    private IDisposable? _autoDismiss;
    private IDisposable? _noteDismiss;
    private bool _openedFromIdle;
    private bool _taskbarShowsError;
    private bool _disposed;

    public StatusCentreCoordinator(
        FileOperationReporter reporter,
        Func<bool> isCommandPaletteOpen,
        IStatusCentreScheduler? scheduler = null,
        IAttentionService? attention = null)
    {
        _reporter = reporter;
        _isCommandPaletteOpen = isCommandPaletteOpen;
        _scheduler = scheduler ?? new DispatcherStatusCentreScheduler();
        _attention = attention;
        _reporter.PropertyChanged += OnReporterPropertyChanged;
        _reporter.Completed.CollectionChanged += OnCompletedChanged;
        _reporter.NoteRaised += OnNoteRaised;
    }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private bool _isPinned;

    [ObservableProperty] private bool _isHovered;

    [ObservableProperty] private bool _hasUnreadFailure;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    public bool HasNote => !string.IsNullOrEmpty(Note);

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
        ClearTaskbarError();
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
        ClearTaskbarError();
    }

    private void OnReporterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        switch (e.PropertyName)
        {
            case nameof(FileOperationReporter.HasActive):
                if (_reporter.HasActive)
                    OnOperationBegan();
                else
                    OnOperationEnded();
                break;
            case nameof(FileOperationReporter.Progress) or nameof(FileOperationReporter.IsIndeterminate):
                if (_reporter.HasActive)
                    ShowTaskbarProgress();
                break;
        }
    }

    private void OnOperationBegan()
    {
        _openedFromIdle = false;
        CancelAutoDismiss();
        _taskbarShowsError = false;
        ShowTaskbarProgress();

        if (!_isCommandPaletteOpen())
            Open(fromUser: false);
    }

    private void OnOperationEnded()
    {
        var latest = _reporter.Completed.Count > 0 ? _reporter.Completed[0] : null;
        if (latest is { Failed: true })
        {
            _attention?.ShowError();
            _taskbarShowsError = true;
            _attention?.NotifyIfInactive(UiStrings.OperationFailedTitle, latest.Message, isError: true);
        }
        else
        {
            _attention?.ClearProgress();
            if (latest is { Succeeded: true })
                _attention?.NotifyIfInactive(UiStrings.OperationCompleteTitle, latest.Message, isError: false);
        }

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

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                HasUnreadFailure = false;
                ClearTaskbarError();
                break;

            // Begin/Complete pairs end through HasActive. A result reported without Begin (a failed
            // terminal launch, branch checkout) never toggles HasActive, so it is handled here or it
            // would land in the list with no badge.
            case NotifyCollectionChangedAction.Add when !_reporter.HasActive:
                OnOperationEnded();
                break;
        }
    }

    private void OnNoteRaised(object? sender, string message)
    {
        if (_disposed)
            return;

        _noteDismiss?.Dispose();
        Note = message;
        _noteDismiss = _scheduler.Schedule(NoteDuration, () =>
        {
            _noteDismiss = null;
            if (!_disposed)
                Note = null;
        });
    }

    private void ShowTaskbarProgress()
        => _attention?.ShowProgress(_reporter.IsIndeterminate ? null : _reporter.Progress);

    private void ClearTaskbarError()
    {
        if (!_taskbarShowsError)
            return;

        _taskbarShowsError = false;
        _attention?.ClearProgress();
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
        _noteDismiss?.Dispose();
        _noteDismiss = null;
        _reporter.PropertyChanged -= OnReporterPropertyChanged;
        _reporter.Completed.CollectionChanged -= OnCompletedChanged;
        _reporter.NoteRaised -= OnNoteRaised;
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
