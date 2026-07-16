using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed partial class FileOperationReporter : ObservableObject, IFileOperationReporter, IDisposable
{
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private CancellationTokenSource? _activeCts;

    [ObservableProperty] private double _progress;

    [ObservableProperty] private bool _isIndeterminate;

    [ObservableProperty] private string _activeTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDetail))]
    private string _activeDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanPauseOperation))]
    [NotifyPropertyChangedFor(nameof(CanResumeOperation))]
    [NotifyPropertyChangedFor(nameof(CanCancelOperation))]
    private bool _hasActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseOperation))]
    [NotifyPropertyChangedFor(nameof(CanResumeOperation))]
    private bool _isPaused;

    public ObservableCollection<OperationEntry> Completed { get; } = new();

    public bool IsBusy => HasActive;

    public bool HasActiveDetail => !string.IsNullOrEmpty(ActiveDetail);

    public bool HasCompleted => Completed.Count > 0;

    public bool CanPauseOperation => HasActive && !IsPaused;

    public bool CanResumeOperation => HasActive && IsPaused;

    public bool CanCancelOperation => HasActive;

    public CancellationToken CancellationToken => _activeCts?.Token ?? CancellationToken.None;

    public void Begin(FileOperationKind kind, int totalItems, string title)
    {
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        _pauseGate.Set();
        IsPaused = false;
        HasActive = true;
        ActiveTitle = title;
        ActiveDetail = string.Empty;
        IsIndeterminate = totalItems <= 0;
        Progress = 0;
        NotifyOperationCommandStates();
    }

    public void Report(FileOperationProgress progress)
    {
        if (!HasActive)
            return;

        if (progress.TotalItems > 0)
        {
            IsIndeterminate = false;
            Progress = (double)progress.CompletedItems / progress.TotalItems;
            ActiveTitle = progress.Kind switch
            {
                FileOperationKind.Copy => $"Copying {progress.CompletedItems} of {progress.TotalItems} items…",
                FileOperationKind.Move => $"Moving {progress.CompletedItems} of {progress.TotalItems} items…",
                FileOperationKind.Delete => $"Deleting {progress.CompletedItems} of {progress.TotalItems} items…",
                _ => ActiveTitle
            };
        }

        if (!string.IsNullOrEmpty(progress.CurrentPath))
            ActiveDetail = Path.GetFileName(progress.CurrentPath);
    }

    public void Complete(FileOperationKind kind, int itemCount, string message)
    {
        EndActiveOperation();
        Progress = 1;
        ActiveDetail = string.Empty;
        AddCompleted(new OperationEntry(message, Failed: false, Succeeded: true, Cancelled: false, Kind: kind, ItemCount: itemCount));
    }

    public void Fail(string message)
    {
        EndActiveOperation();
        ActiveDetail = string.Empty;
        AddCompleted(new OperationEntry(message, Failed: true, Succeeded: false, Cancelled: false, Kind: null, ItemCount: 0));
    }

    public void Cancelled(string message)
    {
        EndActiveOperation();
        ActiveDetail = string.Empty;
        AddCompleted(new OperationEntry(message, Failed: false, Succeeded: false, Cancelled: true, Kind: null, ItemCount: 0));
    }

    public void WaitIfPaused(CancellationToken cancellationToken)
    {
        while (IsPaused)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancellationToken.ThrowIfCancellationRequested();
            _pauseGate.Wait(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private void AddCompleted(OperationEntry entry)
    {
        Completed.Insert(0, entry);
        OnPropertyChanged(nameof(HasCompleted));
        ClearCompletedCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearCompleted() => Completed.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearCompleted))]
    private void ClearCompleted()
    {
        Completed.Clear();
        OnPropertyChanged(nameof(HasCompleted));
        ClearCompletedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPauseOperation))]
    private void PauseOperation()
    {
        if (!HasActive || IsPaused)
            return;

        IsPaused = true;
        _pauseGate.Reset();
        NotifyOperationCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanResumeOperation))]
    private void ResumeOperation()
    {
        if (!HasActive || !IsPaused)
            return;

        IsPaused = false;
        _pauseGate.Set();
        NotifyOperationCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        if (!HasActive)
            return;

        ActiveTitle = "Cancelling operation…";
        _pauseGate.Set();
        IsPaused = false;
        _activeCts?.Cancel();
        NotifyOperationCommandStates();
    }

    partial void OnHasActiveChanged(bool value) => NotifyOperationCommandStates();

    partial void OnIsPausedChanged(bool value) => NotifyOperationCommandStates();

    private void EndActiveOperation()
    {
        HasActive = false;
        IsPaused = false;
        _pauseGate.Set();
        _activeCts?.Dispose();
        _activeCts = null;
        NotifyOperationCommandStates();
    }

    private void NotifyOperationCommandStates()
    {
        PauseOperationCommand.NotifyCanExecuteChanged();
        ResumeOperationCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _activeCts?.Dispose();
        _pauseGate.Dispose();
    }
}

/// <param name="Failed">True if the operation failed (legacy flag kept for bindings/tests).</param>
/// <param name="Succeeded">True if the operation completed successfully. Explicit so the UI can bind a green check vs error glyph.</param>
public sealed record OperationEntry(
    string Message,
    bool Failed,
    bool Succeeded = true,
    bool Cancelled = false,
    FileOperationKind? Kind = null,
    int ItemCount = 0);