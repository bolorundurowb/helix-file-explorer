using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.ViewModels;

/// <summary>
/// State for one file pane: path, history, sort, filter, and the current listing snapshot.
/// </summary>
public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    public const double MinThumbnailSize = 32;
    public const double MaxThumbnailSize = 256;

    private readonly IFileSystemProvider _fileSystem;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private CancellationTokenSource? _refreshCts;
    private bool _disposed;

    public PaneViewModel(IFileSystemProvider fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public event EventHandler? Navigated;
    public event EventHandler<FileSystemEntry>? EntryActivated;

    [ObservableProperty] private string _currentPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    private LayoutMode _viewMode = LayoutMode.Details;
    [ObservableProperty] private SortColumn _sortColumn = SortColumn.Name;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _editablePath = string.Empty;
    [ObservableProperty] private FileSystemEntry? _selectedEntry;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _thumbnailSize = 72;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterActive))]
    private bool _isFilterVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterActive))]
    private string _filterText = string.Empty;

    /// <summary>True when a non-empty quick filter is currently narrowing the listing.</summary>
    public bool IsFilterActive => IsFilterVisible && !string.IsNullOrWhiteSpace(FilterText);

    public bool IsDetailsView => ViewMode == LayoutMode.Details;
    public bool IsListView => ViewMode == LayoutMode.List;
    public bool IsGridView => ViewMode == LayoutMode.Grid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    public ObservableCollection<FileSystemEntry> Entries { get; } = new();

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    partial void OnCurrentPathChanged(string value)
    {
        RebuildBreadcrumbs(value);
        EditablePath = value;
        ClearFilter();
        _ = RefreshAsync();
    }

    partial void OnSortColumnChanged(SortColumn value) => ApplySortAndPublish();

    partial void OnSortDescendingChanged(bool value) => ApplySortAndPublish();

    partial void OnFilterTextChanged(string value) => ApplySortAndPublish();

    partial void OnThumbnailSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, MinThumbnailSize, MaxThumbnailSize);
        if (Math.Abs(clamped - value) > double.Epsilon)
            ThumbnailSize = clamped;
    }

    partial void OnSelectedEntryChanged(FileSystemEntry? value)
        => SelectedCount = value is null ? 0 : 1;

    public void NavigateTo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = ResolveDestination(path);
        if (string.Equals(resolved, CurrentPath, StringComparison.OrdinalIgnoreCase))
            return;

        RecordNavigation(resolved);
    }

    private string ResolveDestination(string path)
    {
        if (path == "..")
        {
            if (string.IsNullOrEmpty(CurrentPath))
                return CurrentPath;

            var parent = Directory.GetParent(CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return parent is null
                ? CurrentPath
                : EnsureTrailingSeparator(parent.FullName);
        }

        var resolved = _fileSystem.ResolvePath(path);
        return EnsureTrailingSeparator(resolved);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (path.Length == 2 && path[1] == ':')
            return path + Path.DirectorySeparatorChar;
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            return path + Path.DirectorySeparatorChar;
        return path;
    }

    private void RecordNavigation(string resolved)
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
        }

        CurrentPath = resolved;
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_backStack.Count == 0)
            return;

        _forwardStack.Push(CurrentPath);
        CurrentPath = _backStack.Pop();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (_forwardStack.Count == 0)
            return;

        _backStack.Push(CurrentPath);
        CurrentPath = _forwardStack.Pop();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    [RelayCommand]
    private void GoUp() => NavigateTo("..");

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || _disposed)
            return;

        var previous = _refreshCts;
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }

        previous?.Dispose();
        var token = cts.Token;

        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

        try
        {
            DirectoryListing listing;
            try
            {
                listing = await _fileSystem.GetDirectoryContentsAsync(CurrentPath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PaneViewModel.RefreshAsync failed for '{CurrentPath}': {ex.Message}");
                listing = DirectoryListing.Empty;
            }

            if (token.IsCancellationRequested || _disposed)
                return;

            _allEntries = listing.Entries;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _disposed)
                    return;

                ApplySortAndPublish();
                Navigated?.Invoke(this, EventArgs.Empty);
            });
        }
finally
            {
                if (ReferenceEquals(_refreshCts, cts))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!_disposed)
                            IsLoading = false;
                    });
                    _refreshCts = null;
                }

                cts.Dispose();
            }
    }

    private void ApplySortAndPublish()
    {
        TotalCount = _allEntries.Count;

        // Filter (allocation-free substring match) then sort into the reusable view buffer.
        FileNameFilter.Apply(_allEntries, IsFilterVisible ? FilterText : null, _viewBuffer);
        _viewBuffer.Sort(FileSystemEntryComparer.For(SortColumn, SortDescending));

        Entries.Clear();
        foreach (var entry in _viewBuffer)
            Entries.Add(entry);

        ItemCount = _viewBuffer.Count;
        SelectedEntry = null;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (IsFilterActive)
        {
            StatusText = $"{ItemCount} of {TotalCount} item{(TotalCount == 1 ? "" : "s")}";
            return;
        }

        StatusText = $"{ItemCount} item{(ItemCount == 1 ? "" : "s")}";
    }

    public void ActivateSelected()
    {
        if (SelectedEntry is { } entry)
            ActivateEntry(entry);
    }

    public void ActivateEntry(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }

        EntryActivated?.Invoke(this, entry);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open '{entry.FullPath}': {ex.Message}");
            StatusText = $"Could not open {entry.Name}";
        }
    }

    public void CommitEditablePath()
    {
        IsEditingPath = false;
        if (!string.IsNullOrWhiteSpace(EditablePath))
            NavigateTo(EditablePath.Trim());
    }

    public void CancelEditablePath()
    {
        IsEditingPath = false;
        EditablePath = CurrentPath;
    }

    public void BeginEditPath()
    {
        EditablePath = CurrentPath;
        IsEditingPath = true;
    }

    // ── Quick filter (Ctrl+F) ────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleFilter()
    {
        if (IsFilterVisible)
            ClearFilter();
        else
            IsFilterVisible = true;
    }

    public void ShowFilter() => IsFilterVisible = true;

    public void ClearFilter()
    {
        var wasFiltering = IsFilterActive;
        IsFilterVisible = false;
        if (FilterText.Length != 0)
            FilterText = string.Empty; // triggers re-publish
        else if (wasFiltering)
            ApplySortAndPublish();
    }

    // ── View mode & thumbnail sizing ─────────────────────────────────────────

    [RelayCommand]
    private void SetViewMode(LayoutMode mode) => ViewMode = mode;

    [RelayCommand]
    private void CycleViewMode()
        => ViewMode = ViewMode switch
        {
            LayoutMode.Details => LayoutMode.List,
            LayoutMode.List => LayoutMode.Grid,
            _ => LayoutMode.Details
        };

    /// <summary>Adjusts grid thumbnail size (used by Ctrl+wheel), clamped to the supported range.</summary>
    public void AdjustThumbnailSize(double delta)
        => ThumbnailSize = Math.Clamp(ThumbnailSize + delta, MinThumbnailSize, MaxThumbnailSize);

    // ── Session snapshot ─────────────────────────────────────────────────────

    public PaneSnapshot CreateSnapshot() => new()
    {
        Path = CurrentPath,
        ViewMode = ViewMode,
        SortColumn = SortColumn,
        SortDescending = SortDescending,
        ThumbnailSize = ThumbnailSize
    };

    /// <summary>Applies persisted presentation state, then navigates to the saved path.</summary>
    public void RestoreFrom(PaneSnapshot snapshot)
    {
        ViewMode = snapshot.ViewMode;
        SortColumn = snapshot.SortColumn;
        SortDescending = snapshot.SortDescending;
        ThumbnailSize = Math.Clamp(snapshot.ThumbnailSize, MinThumbnailSize, MaxThumbnailSize);

        if (!string.IsNullOrWhiteSpace(snapshot.Path))
            NavigateTo(snapshot.Path);
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(path))
            return;

        var parts = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var accumulator = string.Empty;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isDrive = part.Length == 2 && part[1] == ':';
            accumulator = isDrive
                ? part + Path.DirectorySeparatorChar
                : Path.Combine(accumulator.TrimEnd(Path.DirectorySeparatorChar), part) + Path.DirectorySeparatorChar;

            Breadcrumbs.Add(new BreadcrumbSegment(
                DisplayName: isDrive ? part : part,
                Path: accumulator,
                IsLast: i == parts.Length - 1));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _refreshCts?.Cancel(); } catch (ObjectDisposedException) { }
        _refreshCts?.Dispose();
        _refreshCts = null;
    }
}

public readonly record struct BreadcrumbSegment(string DisplayName, string Path, bool IsLast);
