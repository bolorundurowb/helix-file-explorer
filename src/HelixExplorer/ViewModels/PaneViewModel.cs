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

public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    public const double MinThumbnailSize = 32;
    public const double MaxThumbnailSize = 256;

    private readonly IFileSystemProvider _fileSystem;
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IOsFileClipboard _osClipboard;
    private readonly IFileChangeWatcher _watcher;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private CancellationTokenSource? _refreshCts;
    private bool _disposed;

    public PaneViewModel(
        IFileSystemProvider fileSystem,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        IFileChangeWatcher watcher)
    {
        _fileSystem = fileSystem;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _watcher = watcher;
        _watcher.Changed += OnWatcherChanged;
        _clipboard.Changed += OnClipboardChanged;
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
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterActive))]
    private bool _isFilterVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterActive))]
    private string _filterText = string.Empty;

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

    public ObservableCollection<FileSystemEntry> SelectedEntries { get; } = new();

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    partial void OnCurrentPathChanged(string value)
    {
        RebuildBreadcrumbs(value);
        EditablePath = value;
        ClearFilter();
        _watcher.Watch(value);
        PasteCommand.NotifyCanExecuteChanged();
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
    {
        if (value is null)
        {
            SelectedCount = 0;
            return;
        }

        if (SelectedEntries.Count <= 1)
        {
            if (SelectedEntries.Count == 0 || !SelectedEntries[0].Equals(value))
            {
                SelectedEntries.Clear();
                SelectedEntries.Add(value.Value);
            }
            SelectedCount = 1;
        }
    }

    private void OnWatcherChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            if (!_disposed && !string.IsNullOrEmpty(CurrentPath))
                await RefreshAsync();
        });
    }

    public void UpdateSelection(IList<FileSystemEntry> entries)
    {
        SelectedEntries.Clear();
        foreach (var entry in entries)
            SelectedEntries.Add(entry);

        SelectedCount = SelectedEntries.Count;
        if (SelectedEntries.Count == 1)
            SelectedEntry = SelectedEntries[0];
        else
            SelectedEntry = null;
    }

    public void SelectAll()
    {
        SelectedEntries.Clear();
        foreach (var entry in Entries)
            SelectedEntries.Add(entry);

        SelectedCount = SelectedEntries.Count;
        if (SelectedEntries.Count == 1)
            SelectedEntry = SelectedEntries[0];
        else
            SelectedEntry = null;
    }

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

        FileNameFilter.Apply(_allEntries, IsFilterVisible ? FilterText : null, _viewBuffer);
        _viewBuffer.Sort(FileSystemEntryComparer.For(SortColumn, SortDescending));

        Entries.Clear();
        foreach (var entry in _viewBuffer)
            Entries.Add(entry);

        ItemCount = _viewBuffer.Count;
        SelectedEntry = null;
        SelectedEntries.Clear();
        SelectedCount = 0;
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
            FilterText = string.Empty;
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

    public void AdjustThumbnailSize(double delta)
        => ThumbnailSize = Math.Clamp(ThumbnailSize + delta, MinThumbnailSize, MaxThumbnailSize);

    // ── File operations ──────────────────────────────────────────────────────

    private void OnClipboardChanged(object? sender, EventArgs e)
        => PasteCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        _clipboard.SetCut(paths, CurrentPath);
        _ = PublishToOsClipboardAsync(paths, ClipboardOperation.Cut);
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        _clipboard.SetCopy(paths, CurrentPath);
        _ = PublishToOsClipboardAsync(paths, ClipboardOperation.Copy);
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task Paste()
    {
        if (string.IsNullOrEmpty(CurrentPath))
            return;

        try
        {
            var payload = await ResolvePastePayloadAsync().ConfigureAwait(true);
            if (payload is null || payload.Paths.Count == 0)
            {
                StatusText = "Clipboard has no files";
                return;
            }

            if (payload.Operation == ClipboardOperation.Cut)
            {
                await _fileOps.MoveAsync(payload.Paths, CurrentPath).ConfigureAwait(true);
                _clipboard.Clear();
            }
            else
            {
                await _fileOps.CopyAsync(payload.Paths, CurrentPath).ConfigureAwait(true);
            }

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Paste failed: {ex.Message}");
            StatusText = "Paste failed";
        }
    }

    private async Task<ClipboardPayload?> ResolvePastePayloadAsync()
    {
        if (_clipboard.Current is { } internalPayload)
            return internalPayload;

        var os = await _osClipboard.TryGetFilesAsync().ConfigureAwait(true);
        if (os is null)
            return null;

        return new ClipboardPayload(os.Value.Operation, os.Value.Paths, CurrentPath);
    }

    private async Task PublishToOsClipboardAsync(IReadOnlyList<string> paths, ClipboardOperation operation)
    {
        try
        {
            await _osClipboard.SetFilesAsync(paths, operation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OS clipboard publish failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Delete()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        try
        {
            await _fileOps.DeleteAsync(paths, permanently: false);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Delete failed: {ex.Message}");
            StatusText = "Delete failed";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void BeginRename()
    {
        if (SelectedEntries.Count != 1)
            return;

        var entry = SelectedEntries[0];
        RenameText = entry.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task CommitRename()
    {
        if (!IsRenaming || SelectedEntries.Count != 1)
        {
            IsRenaming = false;
            return;
        }

        var entry = SelectedEntries[0];
        var newName = RenameText.Trim();

        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
        {
            IsRenaming = false;
            return;
        }

        try
        {
            await _fileOps.RenameAsync(entry.FullPath, newName);
            IsRenaming = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Rename failed: {ex.Message}");
            StatusText = "Rename failed";
            IsRenaming = false;
        }
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private async Task NewFolder()
    {
        if (string.IsNullOrEmpty(CurrentPath))
            return;

        try
        {
            await _fileOps.CreateFolderAsync(CurrentPath, "New Folder");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NewFolder failed: {ex.Message}");
            StatusText = "Could not create folder";
        }
    }

    private bool HasSelection() => SelectedEntries.Count > 0;

    // Always allow Paste when a folder is open so OS clipboard (Explorer) works;
    // Paste resolves internal payload first, then falls back to IOsFileClipboard.
    private bool CanPaste() => !string.IsNullOrEmpty(CurrentPath);

    private bool CanRename() => SelectedEntries.Count == 1;

    partial void OnSelectedCountChanged(int value)
    {
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
    }

    public async Task HandleDropAsync(IReadOnlyList<string> paths, bool isCopy)
    {
        if (string.IsNullOrEmpty(CurrentPath) || paths.Count == 0)
            return;

        // Ignore drops that would nest an item into itself.
        var filtered = paths
            .Where(p => !IsSameOrChildPath(CurrentPath, p))
            .ToList();
        if (filtered.Count == 0)
            return;

        try
        {
            if (isCopy)
                await _fileOps.CopyAsync(filtered, CurrentPath).ConfigureAwait(true);
            else
                await _fileOps.MoveAsync(filtered, CurrentPath).ConfigureAwait(true);

            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Drop failed: {ex.Message}");
            StatusText = "Drop failed";
        }
    }

    private static bool IsSameOrChildPath(string directory, string path)
    {
        var dir = directory.TrimEnd('\\', '/');
        var candidate = path.TrimEnd('\\', '/');
        if (string.Equals(dir, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = dir + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // ── Session snapshot ─────────────────────────────────────────────────────

    public PaneSnapshot CreateSnapshot() => new()
    {
        Path = CurrentPath,
        ViewMode = ViewMode,
        SortColumn = SortColumn,
        SortDescending = SortDescending,
        ThumbnailSize = ThumbnailSize
    };

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
        _clipboard.Changed -= OnClipboardChanged;
        _watcher.Changed -= OnWatcherChanged;
        _watcher.Dispose();
        try { _refreshCts?.Cancel(); } catch (ObjectDisposedException) { }
        _refreshCts?.Dispose();
        _refreshCts = null;
    }
}

public readonly record struct BreadcrumbSegment(string DisplayName, string Path, bool IsLast);
