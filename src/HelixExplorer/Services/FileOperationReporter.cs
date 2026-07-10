using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed partial class FileOperationReporter : ObservableObject, IFileOperationReporter, IDisposable
{
    [ObservableProperty] private double _progress;

    [ObservableProperty] private bool _isIndeterminate;

    [ObservableProperty] private string _activeTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDetail))]
    private string _activeDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _hasActive;

    public ObservableCollection<OperationEntry> Completed { get; } = new();

    public bool IsBusy => HasActive;

    public bool HasActiveDetail => !string.IsNullOrEmpty(ActiveDetail);

    public bool HasCompleted => Completed.Count > 0;

    public void Begin(FileOperationKind kind, int totalItems, string title)
    {
        HasActive = true;
        ActiveTitle = title;
        ActiveDetail = string.Empty;
        IsIndeterminate = totalItems <= 0;
        Progress = 0;
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
                _ => ActiveTitle
            };
        }

        if (!string.IsNullOrEmpty(progress.CurrentPath))
            ActiveDetail = Path.GetFileName(progress.CurrentPath);
    }

    public void Complete(FileOperationKind kind, int itemCount, string message)
    {
        HasActive = false;
        Progress = 1;
        ActiveDetail = string.Empty;
        AddCompleted(new OperationEntry(message, Failed: false));
    }

    public void Fail(string message)
    {
        HasActive = false;
        ActiveDetail = string.Empty;
        AddCompleted(new OperationEntry(message, Failed: true));
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

    public void Dispose()
    {
    }
}

public sealed record OperationEntry(string Message, bool Failed);
