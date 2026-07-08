using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
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
    private readonly IShellContextMenuService _shellContextMenu;
    private readonly IUiHost _uiHost;
    private readonly IGitProvider _git;
    private readonly IFileChangeWatcher _watcher;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly List<FileSystemEntry> _viewBuffer = new();
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private GitStatusSnapshot _gitSnapshot = GitStatusSnapshot.Empty;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _gitCts;
    private int _refreshGeneration;
    private bool _refreshInFlight;
    private bool _watcherRefreshPending;
    private bool _disposed;

    public PaneViewModel(
        IFileSystemProvider fileSystem,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        IShellContextMenuService shellContextMenu,
        IUiHost uiHost,
        IGitProvider git,
        IFileChangeWatcher watcher)
    {
        _fileSystem = fileSystem;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _shellContextMenu = shellContextMenu;
        _uiHost = uiHost;
        _git = git;
        _watcher = watcher;
        _watcher.Changed += OnWatcherChanged;
        _clipboard.Changed += OnClipboardChanged;
    }

    public event EventHandler? Navigated;
    public event EventHandler<FileSystemEntry>? EntryActivated;
    public event EventHandler<string>? OpenInNewTabRequested;
    public event EventHandler<string>? OpenInNewPaneRequested;

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
    [ObservableProperty] private EntryItemViewModel? _selectedEntry;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _thumbnailSize = 72;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    [ObservableProperty] private GitStatus _repositoryStatus = GitStatus.Empty;
    [ObservableProperty] private bool _isBranchFlyoutOpen;

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

    public ObservableCollection<EntryItemViewModel> Entries { get; } = new();

    public ObservableCollection<EntryItemViewModel> SelectedEntries { get; } = new();

    public ObservableCollection<string> Branches { get; } = new();

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    partial void OnCurrentPathChanged(string value)
    {
        RebuildBreadcrumbs(value);
        EditablePath = value;
        ClearFilter();
        RepositoryStatus = GitStatus.Empty;
        _gitSnapshot = GitStatusSnapshot.Empty;
        CancelGitRefresh();
        _watcher.Watch(value);
        PasteCommand.NotifyCanExecuteChanged();
        _ = RefreshAsync(showLoading: true);
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

    partial void OnSelectedEntryChanged(EntryItemViewModel? value)
    {
        if (value is null)
        {
            SelectedCount = 0;
            return;
        }

        if (SelectedEntries.Count <= 1)
        {
            if (SelectedEntries.Count == 0 || !ReferenceEquals(SelectedEntries[0], value))
            {
                SelectedEntries.Clear();
                SelectedEntries.Add(value);
            }
            SelectedCount = 1;
        }
    }

    private void OnWatcherChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || string.IsNullOrEmpty(CurrentPath))
                return;

            if (_refreshInFlight)
            {
                _watcherRefreshPending = true;
                return;
            }

            _ = RefreshAsync(showLoading: false);
        });
    }

    public void UpdateSelection(IList<EntryItemViewModel> entries)
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
    private Task Refresh() => RefreshAsync(showLoading: true);

    public Task RefreshAsync() => RefreshAsync(showLoading: true);

    public async Task RefreshAsync(bool showLoading)
    {
        if (string.IsNullOrEmpty(CurrentPath) || _disposed)
            return;

        var generation = Interlocked.Increment(ref _refreshGeneration);
        CancelGitRefresh();

        var previous = _refreshCts;
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }

        previous?.Dispose();
        var token = cts.Token;
        var path = CurrentPath;

        _refreshInFlight = true;
        _watcher.Stop();

        if (showLoading)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _refreshGeneration && !_disposed)
                    IsLoading = true;
            });
        }

        try
        {
            DirectoryListing listing;
            try
            {
                listing = await _fileSystem.GetDirectoryContentsAsync(path, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PaneViewModel.RefreshAsync failed for '{path}': {ex.Message}");
                listing = DirectoryListing.Empty;
            }

            if (token.IsCancellationRequested || _disposed || generation != _refreshGeneration)
                return;

            _allEntries = listing.Entries;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _disposed || generation != _refreshGeneration)
                    return;

                ApplySortAndPublish();
                Navigated?.Invoke(this, EventArgs.Empty);

                if (showLoading)
                    IsLoading = false;
            });

            if (token.IsCancellationRequested || _disposed || generation != _refreshGeneration)
                return;

            StartGitStatusRefresh(generation, path);
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                _refreshCts = null;
                _refreshInFlight = false;

                if (!_disposed && !string.IsNullOrEmpty(CurrentPath))
                    _watcher.Watch(CurrentPath);

                if (_watcherRefreshPending && !_disposed)
                {
                    _watcherRefreshPending = false;
                    _ = RefreshAsync(showLoading: false);
                }
            }

            cts.Dispose();
        }
    }

    private void CancelGitRefresh()
    {
        if (_gitCts is null)
            return;

        try { _gitCts.Cancel(); } catch (ObjectDisposedException) { }
        _gitCts.Dispose();
        _gitCts = null;
    }

    private void StartGitStatusRefresh(int generation, string path)
    {
        if (!_git.IsInsideRepository(path))
        {
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _refreshGeneration || _disposed)
                    return;
                if (!PathsEqual(path, CurrentPath))
                    return;

                _gitSnapshot = GitStatusSnapshot.Empty;
                RepositoryStatus = GitStatus.Empty;
                ApplyGitStatusesToEntries();
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _gitCts = cts;
        _ = RefreshGitStatusAsync(generation, path, cts);
    }

    private async Task RefreshGitStatusAsync(int generation, string path, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var snapshot = await _git.GetStatusAsync(path, token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _disposed || generation != _refreshGeneration)
                    return;
                if (!PathsEqual(path, CurrentPath))
                    return;

                _gitSnapshot = snapshot;
                RepositoryStatus = snapshot.Status;
                ApplyGitStatusesToEntries();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Git status failed for '{path}': {ex.Message}");
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

    private void ApplyGitStatusesToEntries()
    {
        foreach (var item in Entries)
        {
            var status = _gitSnapshot.GetStatusForPath(item.FullPath);
            if (item.GitStatus != status)
                item.GitStatus = status;
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            a.TrimEnd('\\', '/'),
            b.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private void ApplySortAndPublish()
    {
        TotalCount = _allEntries.Count;

        FileNameFilter.Apply(_allEntries, IsFilterVisible ? FilterText : null, _viewBuffer);
        _viewBuffer.Sort(FileSystemEntryComparer.For(SortColumn, SortDescending));

        Entries.Clear();
        foreach (var entry in _viewBuffer)
        {
            var gitStatus = _gitSnapshot.GetStatusForPath(entry.FullPath);
            Entries.Add(new EntryItemViewModel(entry, gitStatus));
        }

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
        if (SelectedEntry is { } item)
            ActivateEntry(item);
    }

    public void ActivateEntry(EntryItemViewModel item) => ActivateEntry(item.Entry);

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

            await RefreshAsync(showLoading: false).ConfigureAwait(true);
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
            await RefreshAsync(showLoading: false);
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
            await RefreshAsync(showLoading: false);
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
            await RefreshAsync(showLoading: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NewFolder failed: {ex.Message}");
            StatusText = "Could not create folder";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Open()
    {
        if (SelectedEntries.Count == 1)
            ActivateEntry(SelectedEntries[0]);
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void OpenInNewTab()
    {
        if (SelectedEntries.Count != 1)
            return;

        var entry = SelectedEntries[0];
        OpenInNewTabRequested?.Invoke(this, entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? CurrentPath);
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void OpenInNewWindow()
    {
        if (SelectedEntries.Count != 1)
            return;

        var entry = SelectedEntries[0];
        var path = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenInNewWindow failed: {ex.Message}");
            StatusText = "Could not open new window";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void OpenInNewPane()
    {
        if (SelectedEntries.Count != 1)
            return;

        var entry = SelectedEntries[0];
        OpenInNewPaneRequested?.Invoke(this, entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? CurrentPath);
    }

    [RelayCommand]
    private async Task OpenBranchFlyout()
    {
        if (!RepositoryStatus.IsRepository || string.IsNullOrEmpty(CurrentPath))
            return;

        try
        {
            var branches = await _git.ListBranchesAsync(CurrentPath).ConfigureAwait(true);
            Branches.Clear();
            foreach (var branch in branches)
                Branches.Add(branch);
            IsBranchFlyoutOpen = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenBranchFlyout failed: {ex.Message}");
            StatusText = "Could not list branches";
        }
    }

    [RelayCommand]
    private async Task CheckoutBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrEmpty(CurrentPath))
            return;

        IsBranchFlyoutOpen = false;
        try
        {
            var ok = await _git.CheckoutBranchAsync(CurrentPath, branch).ConfigureAwait(true);
            if (!ok)
            {
                StatusText = $"Could not checkout {branch}";
                return;
            }

            await RefreshAsync(showLoading: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CheckoutBranch failed: {ex.Message}");
            StatusText = $"Could not checkout {branch}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopyPath()
    {
        if (SelectedEntries.Count == 0)
            return;

        try
        {
            var text = string.Join(Environment.NewLine, SelectedEntries.Select(e => e.FullPath));
            await _uiHost.SetClipboardTextAsync(text).ConfigureAwait(true);
            StatusText = SelectedEntries.Count == 1 ? "Path copied" : "Paths copied";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyPath failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private async Task ShowProperties()
    {
        if (SelectedEntries.Count != 1)
            return;

        try
        {
            await _shellContextMenu.ShowPropertiesAsync(
                SelectedEntries[0].FullPath,
                _uiHost.GetMainWindowHandle()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowProperties failed: {ex.Message}");
            StatusText = "Could not open properties";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Share()
    {
        // Windows Share sheet deep integration is deferred post-v1.
        StatusText = "Share is not available yet";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShowMoreOptions()
    {
        if (SelectedEntries.Count == 0 && string.IsNullOrEmpty(CurrentPath))
            return;

        try
        {
            var paths = SelectedEntries.Select(e => e.FullPath).ToList();
            var (x, y) = _uiHost.GetPointerScreenPosition();
            await _shellContextMenu.ShowMoreOptionsAsync(
                CurrentPath,
                paths,
                _uiHost.GetMainWindowHandle(),
                x,
                y).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Show more options failed: {ex.Message}");
            StatusText = "Could not show more options";
        }
    }

    private bool HasSelection() => SelectedEntries.Count > 0;

    private bool HasSingleSelection() => SelectedEntries.Count == 1;

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
        ShowMoreOptionsCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        OpenInNewTabCommand.NotifyCanExecuteChanged();
        OpenInNewWindowCommand.NotifyCanExecuteChanged();
        OpenInNewPaneCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        ShareCommand.NotifyCanExecuteChanged();
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

            await RefreshAsync(showLoading: false).ConfigureAwait(true);
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
        CancelGitRefresh();
        IsLoading = false;
    }
}

public readonly record struct BreadcrumbSegment(string DisplayName, string Path, bool IsLast);
