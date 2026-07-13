using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Sorting;
using HelixExplorer.Services;
using HelixExplorer.Localization;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels;

public sealed partial class PaneViewModel : ObservableObject, IDisposable, IPaneRefreshHost
{
    public const double MinThumbnailSize = 32;
    public const double MaxThumbnailSize = 256;

    private readonly IFileSystemProvider _fileSystem;
    private readonly IArchiveProvider _archive;
    private readonly IFolderColorService _folderColors;
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IShellContextMenuService _shellContextMenu;
    private readonly IUiHost _uiHost;
    private readonly IGitProvider _git;
    private readonly IFileChangeWatcher _watcher;
    private readonly ISettingsStore _settingsStore;
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IUserDialogService _dialogs;
    private readonly IWindowHostService _windowHost;
    private readonly IShellFolderEnumerator _shellEnumerator;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly PaneSelectionModel _selection = new();
    private readonly PaneNavigationController _navigation;
    private readonly PaneListingCoordinator _listing = new();
    private readonly PaneFileOperationCoordinator _fileOperations;
    private readonly PaneRefreshCoordinator _refreshCoordinator;
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private GitStatusSnapshot _gitSnapshot = GitStatusSnapshot.Empty;
    private bool _disposed;

    public PaneViewModel(
        IFileSystemProvider fileSystem,
        IArchiveProvider archive,
        IFolderColorService folderColors,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IShellContextMenuService shellContextMenu,
        IUiHost uiHost,
        IGitProvider git,
        IFileChangeWatcher watcher,
        ISettingsStore settingsStore,
        IQuickAccessProvider quickAccess,
        IUserDialogService dialogs,
        IWindowHostService windowHost,
        IShellFolderEnumerator shell,
        ITerminalLauncher terminalLauncher,
        IPaneCoordinatorFactory coordinatorFactory)
    {
        _fileSystem = fileSystem;
        _archive = archive;
        _folderColors = folderColors;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _shellContextMenu = shellContextMenu;
        _uiHost = uiHost;
        _git = git;
        _watcher = watcher;
        _settingsStore = settingsStore;
        _quickAccess = quickAccess;
        _dialogs = dialogs;
        _windowHost = windowHost;
        _shellEnumerator = shell;
        _terminalLauncher = terminalLauncher;
        _navigation = new PaneNavigationController(fileSystem, archive);
        _fileOperations = coordinatorFactory.CreateFileOperationCoordinator();
        _refreshCoordinator = coordinatorFactory.CreateRefreshCoordinator();
        _watcher.Changed += OnWatcherChanged;
        _clipboard.Changed += OnClipboardChanged;
        _selection.SelectionChanged += OnSelectionModelChanged;
        _selection.SelectedEntries.CollectionChanged += (_, e) => OnSelectedEntriesChanged(_, e);
    }

    private void OnSelectionModelChanged(object? sender, EventArgs e)
    {
        SelectedEntry = _selection.SelectedEntry;
        SelectedCount = _selection.SelectedCount;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyCommandsCanExecuteChanged();
    }

    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncEntrySelectionFlags();
        SelectedCount = SelectedEntries.Count;
    }

    public event EventHandler? Navigated;
    public event EventHandler? SortChanged;
    public event EventHandler? LayoutChanged;
    public event EventHandler<FileSystemEntry>? EntryActivated;
    public event EventHandler<string>? OpenInNewTabRequested;
    public event EventHandler<string>? OpenInOtherPaneRequested;
    public event EventHandler? SelectionChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    [NotifyPropertyChangedFor(nameof(IsMillerView))]
    [NotifyPropertyChangedFor(nameof(IsArchive))]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(IsFileSystem))]
    [NotifyPropertyChangedFor(nameof(IsPathMode))]
    private PaneLocationKind _locationKind = PaneLocationKind.FileSystem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathMode))]
    [NotifyPropertyChangedFor(nameof(IsSearchMode))]
    [NotifyPropertyChangedFor(nameof(IsHomeMode))]
    private OmnibarMode _omnibarMode = OmnibarMode.Path;

    public bool IsPathMode => OmnibarMode == OmnibarMode.Path && IsFileSystem;
    public bool IsSearchMode => OmnibarMode == OmnibarMode.Search;
    public bool IsHomeMode => OmnibarMode == OmnibarMode.Home || IsHome;

    [ObservableProperty] private long _listingSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    [NotifyPropertyChangedFor(nameof(IsMillerView))]
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
    [ObservableProperty] private bool _isSelectionActive = true;
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

    [ObservableProperty] private bool _showHiddenFiles;
    [ObservableProperty] private bool _showFileExtensions = true;

    public bool IsFilterActive => IsFilterVisible && !string.IsNullOrWhiteSpace(FilterText);

    public bool IsDetailsView => ViewMode == LayoutMode.Details;
    public bool IsListView => ViewMode == LayoutMode.List;
    public bool IsGridView => ViewMode == LayoutMode.Grid;
    public bool IsMillerView => ViewMode == LayoutMode.Miller;
    public bool IsArchive => ArchivePath.IsVirtual(CurrentPath);
    public bool IsHome => LocationKind == PaneLocationKind.Home;
    public bool IsFileSystem => LocationKind == PaneLocationKind.FileSystem;
    public bool IsShellNamespace => LocationKind == PaneLocationKind.ShellNamespace;
    public bool IsRecycleBin => ShellPath.IsRecycleBin(CurrentPath);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    public ObservableCollection<EntryItemViewModel> Entries { get; } = new();

    public ObservableCollection<EntryItemViewModel> SelectedEntries => _selection.SelectedEntries;

    public ObservableCollection<string> Branches { get; } = new();

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    partial void OnCurrentPathChanged(string value)
    {
        var isHomeRoute = string.Equals(value, PaneConstants.HomeRoute, StringComparison.Ordinal);
        LocationKind = isHomeRoute
            ? PaneLocationKind.Home
            : ShellPath.IsShellPath(value)
                ? PaneLocationKind.ShellNamespace
                : PaneLocationKind.FileSystem;

        _listing.ClearEntryPool();
        RebuildBreadcrumbs(isHomeRoute ? string.Empty : value);
        EditablePath = isHomeRoute ? string.Empty : value;
        ClearFilter();
        RepositoryStatus = GitStatus.Empty;
        _gitSnapshot = GitStatusSnapshot.Empty;
        _refreshCoordinator.CancelGitRefresh();
        _watcher.Watch(isHomeRoute || IsArchive || ShellPath.IsShellPath(value) ? string.Empty : value);
        PasteCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DeletePermanentlyCommand.NotifyCanExecuteChanged();
        RestoreFromRecycleBinCommand.NotifyCanExecuteChanged();
        EmptyRecycleBinCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
        ShowMoreOptionsCommand.NotifyCanExecuteChanged();
        CompressToZipCommand.NotifyCanExecuteChanged();
        ExtractHereCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        OpenInTerminalCommand.NotifyCanExecuteChanged();

        if (isHomeRoute)
        {
            OmnibarMode = OmnibarMode.Home;
            _allEntries = Array.Empty<FileSystemEntry>();
            Entries.Clear();
            SelectedEntries.Clear();
            SelectedEntry = null;
            SelectedCount = 0;
            ItemCount = 0;
            TotalCount = 0;
            StatusText = string.Empty;
            Navigated?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (OmnibarMode == OmnibarMode.Home)
            OmnibarMode = OmnibarMode.Path;

        _ = RefreshAsync(showLoading: true);
    }

    partial void OnLocationKindChanged(PaneLocationKind value)
    {
        if (value == PaneLocationKind.Home)
            OmnibarMode = OmnibarMode.Home;
        else if (OmnibarMode == OmnibarMode.Home)
            OmnibarMode = OmnibarMode.Path;
    }

    public void NavigateToHome()
    {
        if (IsHome)
            return;

        RecordNavigation(PaneConstants.HomeRoute);
    }

    partial void OnSortColumnChanged(SortColumn value)
    {
        ApplySortAndPublish();
        NotifySortOptionProperties();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSortDescendingChanged(bool value)
    {
        ApplySortAndPublish();
        NotifySortOptionProperties();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsSortByName => SortColumn == SortColumn.Name;
    public bool IsSortByDate => SortColumn == SortColumn.Modified;
    public bool IsSortByType => SortColumn == SortColumn.Type;
    public bool IsSortBySize => SortColumn == SortColumn.Size;
    public bool IsSortAscending => !SortDescending;
    public bool IsSortDescendingActive => SortDescending;

    [RelayCommand]
    private void SetSortColumn(SortColumn column) => SortColumn = column;

    [RelayCommand]
    private void SetSortAscending() => SortDescending = false;

    [RelayCommand]
    private void SetSortDescending() => SortDescending = true;

    private void NotifySortOptionProperties()
    {
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortByDate));
        OnPropertyChanged(nameof(IsSortByType));
        OnPropertyChanged(nameof(IsSortBySize));
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescendingActive));
    }

    partial void OnFilterTextChanged(string value) => ApplySortAndPublish();

    partial void OnThumbnailSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, MinThumbnailSize, MaxThumbnailSize);
        if (Math.Abs(clamped - value) > double.Epsilon)
            ThumbnailSize = clamped;
        else if (IsGridView)
            RequestEntryVisuals();

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnViewModeChanged(LayoutMode value)
    {
        RequestEntryVisuals();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedEntryChanged(EntryItemViewModel? value)
    {
        if (value is null)
            return;

        if (SelectedEntries.Count <= 1)
        {
            if (SelectedEntries.Count == 0 || !ReferenceEquals(SelectedEntries[0], value))
            {
                SelectedEntries.Clear();
                SelectedEntries.Add(value);
            }
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

            _refreshCoordinator.NotifyContentChanged(this);
        });
    }

    public void UpdateSelection(IList<EntryItemViewModel> entries)
        => _selection.UpdateSelection(entries, Entries);

    public void SelectEntry(EntryItemViewModel entry, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
            _selection.Toggle(entry, Entries);
        else if (modifiers.HasFlag(KeyModifiers.Shift) && SelectedEntry is not null)
            _selection.SelectRange(entry, Entries);
        else
            _selection.SelectSingle(entry, Entries);
    }

    public void SelectByBounds(IReadOnlyList<EntryItemViewModel> hits, bool additive)
        => _selection.SelectByBounds(hits, Entries, additive);

    private void SyncEntrySelectionFlags()
    {
        var selected = new HashSet<EntryItemViewModel>(SelectedEntries);
        foreach (var entry in Entries)
        {
            var isSelected = selected.Contains(entry);
            if (entry.IsSelected != isSelected)
                entry.IsSelected = isSelected;

            RefreshCutState(entry);
        }
    }

    public void RefreshCutState()
    {
        foreach (var entry in Entries)
            RefreshCutState(entry);
    }

    private void RefreshCutState(EntryItemViewModel entry)
    {
        var isCut = ClipboardCutState.IsPathCut(_clipboard, entry.FullPath);
        if (entry.IsCut != isCut)
            entry.IsCut = isCut;
    }

    public void SelectAll()
        => _selection.SelectAll(Entries);

    public bool HasSelectionForOps => CanModifySelection();

    public bool HasSingleSelectionForOps => CanRename();

    public bool CanPasteHere => CanPaste();

    public bool CanCreateFolderHere => CanModifyHere() && !string.IsNullOrEmpty(CurrentPath);

    public bool CanSelectAllHere => CanModifyHere() && Entries.Count > 0;

    public void NavigateTo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = _navigation.ResolveDestination(path, CurrentPath);
        if (string.Equals(resolved, CurrentPath, StringComparison.OrdinalIgnoreCase))
            return;

        RecordNavigation(resolved);
    }

    private void RecordNavigation(string resolved)
    {
        var transition = _navigation.RecordForward(CurrentPath, resolved);
        CurrentPath = transition.Path;
        CanGoBack = transition.CanGoBack;
        CanGoForward = transition.CanGoForward;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        var transition = _navigation.GoBack(CurrentPath);
        if (transition is null)
            return;

        CurrentPath = transition.Value.Path;
        CanGoBack = transition.Value.CanGoBack;
        CanGoForward = transition.Value.CanGoForward;
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        var transition = _navigation.GoForward(CurrentPath);
        if (transition is null)
            return;

        CurrentPath = transition.Value.Path;
        CanGoBack = transition.Value.CanGoBack;
        CanGoForward = transition.Value.CanGoForward;
    }

    [RelayCommand]
    private void GoUp()
    {
        if (IsHome)
            return;

        if (IsShellNamespace)
        {
            NavigateToHome();
            return;
        }

        NavigateTo("..");
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync(showLoading: true);

    public Task RefreshAsync() => RefreshAsync(showLoading: true);

    public Task RefreshAsync(bool showLoading)
        => _refreshCoordinator.RefreshAsync(this, showLoading);

    public void RefreshFolderColorBindings()
    {
        foreach (var item in Entries)
        {
            if (item.IsDirectory)
                item.NotifyFolderColorChanged();
        }
    }

    private void ApplySortAndPublish()
        => ((IPaneRefreshHost)this).ApplySortAndPublish(new ListingPublishRequest
        {
            AllEntries = _allEntries,
            GitSnapshot = _gitSnapshot,
            ShowHiddenFiles = ShowHiddenFiles,
            ShowFileExtensions = ShowFileExtensions,
            IsFilterVisible = IsFilterVisible,
            FilterText = FilterText,
            SortColumn = SortColumn,
            SortDescending = SortDescending
        });

    ListingPublishResult IPaneRefreshHost.ApplySortAndPublish(ListingPublishRequest request)
    {
        var result = _listing.ApplySortAndPublish(request);

        TotalCount = result.TotalCount;
        ListingSizeBytes = result.ListingSizeBytes;
        SyncEntriesCollection(result.Entries);
        RefreshCutState();
        ItemCount = result.ItemCount;
        SelectedEntry = null;
        SelectedEntries.Clear();
        SelectedCount = 0;
        UpdateStatusText();
        _refreshCoordinator.RequestEntryVisuals(this, result.VisualTargets);
        return result;
    }

    private void SyncEntriesCollection(IReadOnlyList<EntryItemViewModel> nextEntries)
    {
        if (Entries.Count == nextEntries.Count)
        {
            var sameOrder = true;
            for (var i = 0; i < nextEntries.Count; i++)
            {
                if (ReferenceEquals(Entries[i], nextEntries[i]))
                    continue;

                sameOrder = false;
                break;
            }

            if (sameOrder)
                return;
        }

        Entries.Clear();
        foreach (var item in nextEntries)
            Entries.Add(item);
    }

    private void RequestEntryVisuals(IReadOnlyList<EntryItemViewModel>? targets = null)
        => _refreshCoordinator.RequestEntryVisuals(this, targets);

    private void UpdateStatusText()
    {
        if (IsFilterActive)
        {
            StatusText = $"{ItemCount} of {TotalCount} items";
            return;
        }

        var size = FileSizeFormatter.FormatBinary(ListingSizeBytes);
        StatusText = $"{ItemCount} item{(ItemCount == 1 ? "" : "s")} | {size}";
    }

    public void ActivateSelected()
    {
        if (SelectedEntry is { } item)
            ActivateEntry(item);
    }

    public void ActivateEntry(EntryItemViewModel item) => ActivateEntry(item.Entry);

    public void ActivateEntry(FileSystemEntry entry)
    {
        if (entry.IsDirectory || _archive.IsArchiveFile(entry.FullPath))
        {
            NavigateTo(entry.FullPath);
            return;
        }

        EntryActivated?.Invoke(this, entry);
    }

    public async Task<IReadOnlyList<EntryItemViewModel>> EnumerateMillerChildrenAsync(string path)
    {
        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            if (ArchivePath.IsVirtual(path))
                entries = await _archive.EnumerateAsync(path).ConfigureAwait(true);
            else
                entries = (await _fileSystem.GetDirectoryContentsAsync(path).ConfigureAwait(true)).Entries;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnumerateMillerChildrenAsync failed for '{path}': {ex.Message}");
            entries = Array.Empty<FileSystemEntry>();
        }

        var buffer = new List<FileSystemEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (!ShowHiddenFiles && entry.IsHidden)
                continue;

            buffer.Add(entry);
        }

        buffer.Sort(FileSystemEntryComparer.For(SortColumn, SortDescending));
        return buffer.ConvertAll(entry => new EntryItemViewModel(entry, ShowFileExtensions));
    }

    public void ApplyViewSettings(bool showHiddenFiles, bool showFileExtensions)
    {
        var hiddenChanged = ShowHiddenFiles != showHiddenFiles;
        var extensionsChanged = ShowFileExtensions != showFileExtensions;
        ShowHiddenFiles = showHiddenFiles;
        ShowFileExtensions = showFileExtensions;

        if (hiddenChanged)
            ApplySortAndPublish();
        else if (extensionsChanged)
            RefreshDisplayNames();
    }

    public void RefreshListingPresentation() => ApplySortAndPublish();

    private void RefreshDisplayNames()
    {
        foreach (var entry in Entries)
            entry.SetShowFileExtensions(ShowFileExtensions);
    }

    public async Task<IReadOnlyList<string>> ResolvePhysicalPathsAsync(IReadOnlyList<string> paths)
    {
        var result = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (ArchivePath.IsVirtual(path))
            {
                var extracted = await _archive.ExtractEntryAsync(path).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(extracted))
                    result.Add(extracted);
            }
            else if (File.Exists(path) || Directory.Exists(path))
            {
                result.Add(path);
            }
        }

        return result;
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

    [RelayCommand]
    private void ToggleFilter() => EnterSearchMode();

    [RelayCommand]
    public void EnterSearchMode()
    {
        if (!IsFileSystem)
            return;

        OmnibarMode = OmnibarMode.Search;
        IsFilterVisible = true;
    }

    [RelayCommand]
    public void ExitSearchMode()
    {
        OmnibarMode = IsHome ? OmnibarMode.Home : OmnibarMode.Path;
        ClearFilter();
    }

    public void ShowFilter() => EnterSearchMode();

    public void ClearFilter()
    {
        var wasFiltering = IsFilterActive;
        IsFilterVisible = false;
        if (FilterText.Length != 0)
            FilterText = string.Empty;
        else if (wasFiltering)
            ApplySortAndPublish();

        if (OmnibarMode == OmnibarMode.Search)
            OmnibarMode = IsHome ? OmnibarMode.Home : OmnibarMode.Path;
    }

    [RelayCommand]
    public void SetViewMode(LayoutMode mode) => ViewMode = mode;

    [RelayCommand]
    private void CycleViewMode()
        => ViewMode = ViewMode switch
        {
            LayoutMode.Details => LayoutMode.List,
            LayoutMode.List => LayoutMode.Grid,
            LayoutMode.Grid => LayoutMode.Miller,
            _ => LayoutMode.Details
        };

    public void AdjustThumbnailSize(double delta)
        => ThumbnailSize = Math.Clamp(ThumbnailSize + delta, MinThumbnailSize, MaxThumbnailSize);

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        RefreshCutState();
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private void Cut()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        _clipboard.SetCut(paths, CurrentPath);
        _ = _fileOperations.PublishToOsClipboardAsync(paths, ClipboardOperation.Cut);
        RefreshCutState();
        NotifyCommandsCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private void Copy()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        _clipboard.SetCopy(paths, CurrentPath);
        _ = _fileOperations.PublishToOsClipboardAsync(paths, ClipboardOperation.Copy);
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task Paste()
        => _fileOperations.PasteAsync(
            CurrentPath,
            () => RefreshAsync(showLoading: false),
            t => StatusText = t);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task Delete()
    {
        if (SelectedEntries.Count == 0)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        try
        {
            var result = await _fileOps.DeleteAsync(paths, permanently: false);
            await RefreshAsync(showLoading: false);
            await FileOperationUiHelper.ReportResultAsync(_dialogs, result, "Delete", t => StatusText = t);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Delete failed: {ex.Message}");
            await _dialogs.ShowErrorAsync(UiStrings.DeleteFailed, ex.Message);
            StatusText = UiStrings.DeleteFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task DeletePermanently()
    {
        if (SelectedEntries.Count == 0)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            UiStrings.PermanentlyDeleteTitle,
            UiStrings.PermanentlyDeleteMessage);
        if (!confirmed)
            return;

        var paths = SelectedEntries.Select(e => e.FullPath).ToList();
        try
        {
            var result = await _fileOps.DeleteAsync(paths, permanently: true);
            await RefreshAsync(showLoading: false);
            await FileOperationUiHelper.ReportResultAsync(_dialogs, result, "Permanent delete", t => StatusText = t);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Permanent delete failed: {ex.Message}");
            await _dialogs.ShowErrorAsync(UiStrings.DeleteFailed, ex.Message);
            StatusText = UiStrings.DeleteFailed;
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
            var oldPath = entry.FullPath;
            var result = await _fileOps.RenameAsync(oldPath, newName);
            if (result.Failed > 0)
            {
                await _dialogs.ShowErrorAsync(UiStrings.RenameFailed, result.Failures[0].Message);
                StatusText = UiStrings.RenameFailed;
                IsRenaming = false;
                return;
            }

            _listing.RemoveFromPool(oldPath);
            IsRenaming = false;
            await RefreshAsync(showLoading: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Rename failed: {ex.Message}");
            StatusText = UiStrings.RenameFailed;
            IsRenaming = false;
        }
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand(CanExecute = nameof(CanModifyHere))]
    private async Task NewFolder()
    {
        if (string.IsNullOrEmpty(CurrentPath) || IsArchive)
            return;

        try
        {
            await _fileOps.CreateFolderAsync(CurrentPath, "New Folder");
            await RefreshAsync(showLoading: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NewFolder failed: {ex.Message}");
            StatusText = UiStrings.NewFolderFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompressSelection))]
    private async Task CompressToZip()
    {
        var sources = SelectedEntries
            .Select(e => e.FullPath)
            .Where(path => !ArchivePath.IsVirtual(path))
            .ToList();
        if (sources.Count == 0 || string.IsNullOrEmpty(CurrentPath))
            return;

        var hostDir = PaneFileOperationCoordinator.GetPhysicalHostDirectory(CurrentPath, IsArchive);
        if (string.IsNullOrEmpty(hostDir))
            return;

        var zipName = sources.Count == 1
            ? Path.GetFileNameWithoutExtension(sources[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".zip"
            : "Archive.zip";
        var destination = PaneFileOperationCoordinator.GetUniquePath(Path.Combine(hostDir, zipName));

        try
        {
            IsLoading = true;
            await _archive.CreateZipAsync(sources, destination).ConfigureAwait(true);
            await RefreshAsync(showLoading: false).ConfigureAwait(true);
            IsLoading = false;
            StatusText = UiStrings.CreatedArchive(Path.GetFileName(destination));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CompressToZip failed: {ex.Message}");
            StatusText = UiStrings.CompressToZipFailed;
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtractSelection))]
    private async Task ExtractHere()
    {
        var physicalArchives = SelectedEntries
            .Select(e => e.FullPath)
            .Where(path => !ArchivePath.IsVirtual(path) && _archive.IsArchiveFile(path))
            .ToList();
        var virtualPaths = SelectedEntries
            .Select(e => e.FullPath)
            .Where(ArchivePath.IsVirtual)
            .ToList();

        if (physicalArchives.Count == 0 && virtualPaths.Count == 0)
            return;

        try
        {
            IsLoading = true;

            foreach (var archivePath in physicalArchives)
            {
                var hostDir = Path.GetDirectoryName(archivePath);
                if (string.IsNullOrEmpty(hostDir))
                    continue;

                var destination = PaneFileOperationCoordinator.GetUniqueDirectory(Path.Combine(
                    hostDir,
                    Path.GetFileNameWithoutExtension(archivePath)));
                await _archive.ExtractArchiveToDirectoryAsync(archivePath, destination).ConfigureAwait(true);
            }

            if (virtualPaths.Count > 0)
            {
                var hostDir = PaneFileOperationCoordinator.GetPhysicalHostDirectory(CurrentPath, IsArchive);
                if (string.IsNullOrEmpty(hostDir))
                {
                    StatusText = "Could not determine extract location";
                    IsLoading = false;
                    return;
                }

                await _archive.ExtractVirtualEntriesAsync(virtualPaths, hostDir).ConfigureAwait(true);
            }

            if (!IsArchive)
                await RefreshAsync(showLoading: false).ConfigureAwait(true);
            else
                IsLoading = false;

            StatusText = UiStrings.Extracted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExtractHere failed: {ex.Message}");
            StatusText = UiStrings.ExtractFailed;
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetFolderColor))]
    private void SetFolderColor(string? hex)
    {
        if (SelectedEntries.Count != 1 || string.IsNullOrWhiteSpace(hex))
            return;

        var path = SelectedEntries[0].FullPath;
        if (!SelectedEntries[0].IsDirectory || ArchivePath.IsVirtual(path))
            return;

        var color = Avalonia.Media.Color.Parse(hex);
        _folderColors.SetColor(path, color.ToUInt32());
        StatusText = UiStrings.FolderColored(SelectedEntries[0].Name);
    }

    [RelayCommand(CanExecute = nameof(CanSetFolderColor))]
    private void ClearFolderColor()
    {
        if (SelectedEntries.Count != 1)
            return;

        var path = SelectedEntries[0].FullPath;
        _folderColors.RemoveColor(path);
        StatusText = UiStrings.FolderColorCleared(SelectedEntries[0].Name);
    }

    private bool CanSetFolderColor()
        => CanModifySelection()
           && SelectedEntries.Count == 1
           && SelectedEntries[0].IsDirectory
           && !ArchivePath.IsVirtual(SelectedEntries[0].FullPath);

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
        _ = _windowHost.OpenWindowAsync(path, restoreSession: false);
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void OpenInOtherPane()
    {
        if (SelectedEntries.Count != 1)
            return;

        var entry = SelectedEntries[0];
        OpenInOtherPaneRequested?.Invoke(this, entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? CurrentPath);
    }

    [RelayCommand(CanExecute = nameof(CanOpenInTerminal))]
    private void OpenInTerminal()
    {
        var dirPath = SelectedEntries.Count == 1 && SelectedEntries[0].IsDirectory
            ? SelectedEntries[0].FullPath
            : CurrentPath;

        if (!_terminalLauncher.TryOpenInDirectory(dirPath))
        {
            Debug.WriteLine("OpenInTerminal failed: no compatible terminal found");
            StatusText = UiStrings.OpenInTerminalFailed;
        }
    }

    private bool CanOpenInTerminal()
    {
        if (IsArchive || IsHome || string.IsNullOrEmpty(CurrentPath))
            return false;

        if (SelectedEntries.Count == 0)
            return true;

        if (SelectedEntries.Count == 1 && SelectedEntries[0].IsDirectory)
            return true;

        return false;
    }

    [RelayCommand(CanExecute = nameof(CanPinSelection))]
    private void PinToSidebar()
    {
        if (SelectedEntries.Count != 1 || !SelectedEntries[0].IsDirectory)
            return;

        PinPathRequested?.Invoke(this, (SelectedEntries[0].FullPath, true));
    }

    [RelayCommand(CanExecute = nameof(CanUnpinSelection))]
    private void UnpinFromSidebar()
    {
        if (SelectedEntries.Count != 1 || !SelectedEntries[0].IsDirectory)
            return;

        PinPathRequested?.Invoke(this, (SelectedEntries[0].FullPath, false));
    }

    public event EventHandler<(string Path, bool Pin)>? PinPathRequested;

    public void NotifyPinStateChanged()
    {
        PinToSidebarCommand.NotifyCanExecuteChanged();
        UnpinFromSidebarCommand.NotifyCanExecuteChanged();
    }

    private bool CanPinSelection()
    {
        if (SelectedEntries.Count != 1 || !SelectedEntries[0].IsDirectory)
            return false;

        var settings = _settingsStore.Load();
        var defaults = GetDefaultPinnedPaths();
        return !PinnedPathHelper.IsPinnedOrDefault(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            defaults,
            SelectedEntries[0].FullPath);
    }

    private bool CanUnpinSelection()
    {
        if (SelectedEntries.Count != 1 || !SelectedEntries[0].IsDirectory)
            return false;

        var settings = _settingsStore.Load();
        var defaults = GetDefaultPinnedPaths();
        return PinnedPathHelper.IsVisibleInSidebar(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            defaults,
            SelectedEntries[0].FullPath);
    }

    private IReadOnlyList<string> GetDefaultPinnedPaths()
        => _quickAccess.GetPinnedDefaults()
            .Select(t => t.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

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
            StatusText = UiStrings.ListBranchesFailed;
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
                StatusText = UiStrings.CheckoutBranchFailed(branch);
                return;
            }

            await RefreshAsync(showLoading: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CheckoutBranch failed: {ex.Message}");
            StatusText = UiStrings.CheckoutBranchFailed(branch);
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
            StatusText = SelectedEntries.Count == 1 ? UiStrings.PathCopied : UiStrings.PathsCopied;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyPath failed: {ex.Message}");
            StatusText = UiStrings.CopyPathFailed;
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
            StatusText = UiStrings.ShowPropertiesFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowMoreOptions))]
    private async Task ShowMoreOptions()
    {
        if (IsArchive)
            return;

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
            StatusText = UiStrings.ShowMoreOptionsFailed;
        }
    }

    private bool HasSelection() => SelectedEntries.Count > 0;

    private bool HasSingleSelection() => SelectedEntries.Count == 1;

    private bool CanModifyHere() => !IsArchive && !IsHome && !IsShellNamespace;

    private bool CanModifySelection() => (CanModifyHere() || IsRecycleBin) && HasSelection();

    [RelayCommand(CanExecute = nameof(CanRestoreFromRecycleBin))]
    private async Task RestoreFromRecycleBin()
    {
        if (!IsRecycleBin || SelectedEntries.Count == 0)
            return;

        try
        {
            foreach (var entry in SelectedEntries)
                await _shellEnumerator.RestoreAsync(entry.FullPath).ConfigureAwait(true);

            await RefreshAsync(showLoading: false);
            StatusText = UiStrings.RestoredFromRecycleBin;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(UiStrings.RestoreFailed, ex.Message);
            StatusText = UiStrings.RestoreFailed;
        }
    }

    private bool CanRestoreFromRecycleBin() => IsRecycleBin && HasSelection();

    [RelayCommand(CanExecute = nameof(CanEmptyRecycleBin))]
    private async Task EmptyRecycleBin()
    {
        if (!IsRecycleBin)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            UiStrings.EmptyRecycleBinTitle,
            UiStrings.EmptyRecycleBinMessage);
        if (!confirmed)
            return;

        try
        {
            await _shellEnumerator.EmptyRecycleBinAsync().ConfigureAwait(true);
            await RefreshAsync(showLoading: false);
            StatusText = UiStrings.RecycleBinEmptied;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(UiStrings.EmptyRecycleBinFailed, ex.Message);
        }
    }

    private bool CanEmptyRecycleBin() => IsRecycleBin;

    private bool CanShowMoreOptions() => CanModifyHere() && (HasSelection() || !string.IsNullOrEmpty(CurrentPath));

    // Always allow Paste when a folder is open so OS clipboard (Explorer) works;
    // Paste resolves internal payload first, then falls back to IOsFileClipboard.
    private bool CanPaste() => CanModifyHere() && !string.IsNullOrEmpty(CurrentPath);

    private bool CanRename() => CanModifySelection() && SelectedEntries.Count == 1;

    private bool CanCompressSelection()
        => CanModifySelection()
           && SelectedEntries.All(e => !ArchivePath.IsVirtual(e.FullPath));

    private bool CanExtractSelection()
        => HasSelection()
           && SelectedEntries.Any(e => _archive.IsArchiveFile(e.FullPath) || ArchivePath.IsVirtual(e.FullPath));

    partial void OnSelectedCountChanged(int value)
        => NotifyCommandsCanExecuteChanged();

    public void NotifyCommandsCanExecuteChanged()
    {
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DeletePermanentlyCommand.NotifyCanExecuteChanged();
        RestoreFromRecycleBinCommand.NotifyCanExecuteChanged();
        EmptyRecycleBinCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
        ShowMoreOptionsCommand.NotifyCanExecuteChanged();
        CompressToZipCommand.NotifyCanExecuteChanged();
        ExtractHereCommand.NotifyCanExecuteChanged();
        SetFolderColorCommand.NotifyCanExecuteChanged();
        ClearFolderColorCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        OpenInNewTabCommand.NotifyCanExecuteChanged();
        OpenInNewWindowCommand.NotifyCanExecuteChanged();
        OpenInOtherPaneCommand.NotifyCanExecuteChanged();
        PinToSidebarCommand.NotifyCanExecuteChanged();
        UnpinFromSidebarCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        OpenInTerminalCommand.NotifyCanExecuteChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task HandleDropAsync(IReadOnlyList<string> paths, bool isCopy)
    {
        if (IsArchive)
            return Task.CompletedTask;

        return _fileOperations.HandleDropAsync(
            CurrentPath,
            paths,
            isCopy,
            () => RefreshAsync(showLoading: false),
            t => StatusText = t);
    }

    public PaneSnapshot CreateSnapshot() => new()
    {
        Path = IsHome ? string.Empty : CurrentPath,
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
        else
            NavigateToHome();
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        foreach (var crumb in PaneNavigationController.BuildBreadcrumbs(path))
            Breadcrumbs.Add(crumb);
    }

    bool IPaneRefreshHost.IsDisposed => _disposed;

    string IPaneRefreshHost.CurrentPath => CurrentPath;

    bool IPaneRefreshHost.IsHome => IsHome;

    bool IPaneRefreshHost.IsArchive => IsArchive;

    bool IPaneRefreshHost.IsShellNamespace => IsShellNamespace;

    bool IPaneRefreshHost.ShowHiddenFiles => ShowHiddenFiles;

    bool IPaneRefreshHost.ShowFileExtensions => ShowFileExtensions;

    bool IPaneRefreshHost.IsFilterVisible => IsFilterVisible;

    string IPaneRefreshHost.FilterText => FilterText;

    SortColumn IPaneRefreshHost.SortColumn => SortColumn;

    bool IPaneRefreshHost.SortDescending => SortDescending;

    bool IPaneRefreshHost.IsGridView => IsGridView;

    double IPaneRefreshHost.ThumbnailSize => ThumbnailSize;

    LayoutMode IPaneRefreshHost.ViewMode => ViewMode;

    IReadOnlyList<EntryItemViewModel> IPaneRefreshHost.Entries => Entries;

    void IPaneRefreshHost.SetLoading(bool loading) => IsLoading = loading;

    void IPaneRefreshHost.SetStatusText(string text) => StatusText = text;

    void IPaneRefreshHost.OnNavigated() => Navigated?.Invoke(this, EventArgs.Empty);

    void IPaneRefreshHost.ApplyGitSnapshot(GitStatusSnapshot snapshot, GitStatus status)
    {
        _gitSnapshot = snapshot;
        RepositoryStatus = status;
        ApplyGitStatusesToEntries();
    }

    void IPaneRefreshHost.StopWatcher() => _watcher.Stop();

    void IPaneRefreshHost.RestartWatcher()
    {
        if (!_disposed && !string.IsNullOrEmpty(CurrentPath) && !IsArchive && !IsShellNamespace)
            _watcher.Watch(CurrentPath);
    }

    void IPaneRefreshHost.RequestRefresh()
        => _ = RefreshAsync(showLoading: false);

    private void ApplyGitStatusesToEntries()
    {
        foreach (var item in Entries)
        {
            var status = _gitSnapshot.GetStatusForPath(item.FullPath);
            if (item.GitStatus != status)
                item.GitStatus = status;
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
        _refreshCoordinator.Dispose();
        IsLoading = false;
    }
}

public readonly record struct BreadcrumbSegment(string DisplayName, string Path, bool IsLast);
