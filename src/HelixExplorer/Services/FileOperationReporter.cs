using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed partial class FileOperationReporter : ObservableObject, IFileOperationReporter, IDisposable
{
    private CancellationTokenSource? _dismissCts;

    [ObservableProperty] private FileOperationPhase _phase = FileOperationPhase.None;

    [ObservableProperty] private double _progress;

    [ObservableProperty] private bool _isIndeterminate;

    [ObservableProperty] private string _title = string.Empty;

    [ObservableProperty] private string _detail = string.Empty;

    public bool IsVisible => Phase != FileOperationPhase.None;

    public bool IsInProgress => Phase == FileOperationPhase.InProgress;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    partial void OnPhaseChanged(FileOperationPhase value)
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsInProgress));
    }

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(HasDetail));

    [RelayCommand]
    private void Dismiss() => DismissBanner();

    public void Begin(FileOperationKind kind, int totalItems, string title)
    {
        CancelDismiss();
        Phase = FileOperationPhase.InProgress;
        Title = title;
        Detail = string.Empty;
        IsIndeterminate = totalItems <= 0;
        Progress = 0;
    }

    public void Report(FileOperationProgress progress)
    {
        if (Phase != FileOperationPhase.InProgress)
            return;

        if (progress.TotalItems > 0)
        {
            IsIndeterminate = false;
            Progress = (double)progress.CompletedItems / progress.TotalItems;
            Title = progress.Kind switch
            {
                FileOperationKind.Copy => $"Copying {progress.CompletedItems} of {progress.TotalItems} items…",
                FileOperationKind.Move => $"Moving {progress.CompletedItems} of {progress.TotalItems} items…",
                _ => Title
            };
        }

        if (!string.IsNullOrEmpty(progress.CurrentPath))
            Detail = Path.GetFileName(progress.CurrentPath);
    }

    public void Complete(FileOperationKind kind, int itemCount, string message)
    {
        CancelDismiss();
        Phase = FileOperationPhase.Completed;
        Progress = 1;
        IsIndeterminate = false;
        Title = message;
        Detail = string.Empty;
        ScheduleDismiss();
    }

    public void Fail(string message)
    {
        CancelDismiss();
        Phase = FileOperationPhase.Failed;
        Title = message;
        Detail = string.Empty;
        ScheduleDismiss(TimeSpan.FromSeconds(6));
    }

    public void DismissBanner()
    {
        CancelDismiss();
        Phase = FileOperationPhase.None;
        Progress = 0;
        Title = string.Empty;
        Detail = string.Empty;
    }

    private void ScheduleDismiss(TimeSpan? delay = null)
    {
        _dismissCts = new CancellationTokenSource();
        var token = _dismissCts.Token;
        var ms = (int)(delay ?? TimeSpan.FromSeconds(4)).TotalMilliseconds;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ms, token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(DismissBanner);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void CancelDismiss()
    {
        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _dismissCts = null;
    }

    public void Dispose() => CancelDismiss();
}
