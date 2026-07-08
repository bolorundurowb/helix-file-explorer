using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Infrastructure;
using HelixExplorer.Models;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>State for one file pane. Each pane owns its own change-watcher and its own
/// navigation history, so left/right panes are fully independent.</summary>
public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    private readonly FileChangeWatcherService _watcher = new();
    private readonly IFileSystemService _fileSystem;
    private readonly IArchiveService _archive;
    private readonly IGitService _git;
    private readonly AsyncDebouncer _filterDebouncer = new(TimeSpan.FromMilliseconds(150));
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private IReadOnlyList<FileSystemEntry> _filteredEntries = Array.Empty<FileSystemEntry>();
    private FileSystemEntry? _selected;
    private CancellationTokenSource? _refreshCts;

    public PaneViewModel(IFileSystemService fileSystem, IArchiveService archive, IGitService git)
    {
        _fileSystem = fileSystem;
        _archive = archive;
        _git = git;
        _thumbnailSize = ServiceLocator.Settings.ThumbnailSize;
        ServiceLocator.Settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(FilteredEntries));

    /// <summary>Raised when the user activates a file entry (double-click or Enter).</summary>
    public event EventHandler<FileSystemEntry>? EntryActivated;

    /// <summary>Fired when navigation completes — used by Tab/StatusBar to refresh derived info.</summary>
    public event EventHandler? Navigated;

    /// <summary>Raised when the user asks to open the current selection in the sibling pane.</summary>
    public event EventHandler<FileSystemEntry>? OpenInOtherPaneRequested;

    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private LayoutMode _viewMode = LayoutMode.Details;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _isArchive;
    [ObservableProperty] private GitStatus _repositoryStatus = GitStatus.Empty;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private double _thumbnailSize;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _sortColumn = "Name";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _isBranchFlyoutOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    /// <summary>Local branches, populated when the branch flyout opens.</summary>
    public ObservableCollection<string> Branches { get; } = new();

    public IReadOnlyList<FileSystemEntry> FilteredEntries => _filteredEntries;

    public FileSystemEntry? SelectedEntry
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    partial void OnFilterTextChanged(string value) => ScheduleFilter();

    partial void OnThumbnailSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, 32, 256);
        if (clamped != value) { ThumbnailSize = clamped; return; }
        ServiceLocator.Settings.ThumbnailSize = clamped;
    }

    partial void OnCurrentPathChanged(string value)
    {
        IsArchive = value.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase);
        _ = RefreshAsync();
    }

    /// <summary>Adjusts the grid thumbnail size (Ctrl+wheel). Clamped to 32–256 px.</summary>
    public void AdjustThumbnail(double delta) => ThumbnailSize = Math.Clamp(ThumbnailSize + delta, 32, 256);

    /// <summary>Drives path semantics; resolves .., and archive:// schemes.</summary>
    public void NavigateTo(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var resolved = ResolveDestination(path);
        if (resolved == CurrentPath) return;
        RecordNavigation(resolved);
    }

    private string ResolveDestination(string path)
    {
        if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return path.EndsWith('/') ? path : path + "/";
        }

        // Mount archives by navigating INTO them.
        if (_archive.IsArchive(path))
        {
            return ArchiveService.Scheme + path + "!";
        }

        // Relative .. handling.
        if (path == "..")
        {
            var cp = CurrentPath;
            if (cp.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                var bang = cp.IndexOf('!');
                if (bang < 0)
                {
                    // archive://X! → leave the archive.
                    return cp[ArchiveService.Scheme.Length..];
                }
                var inner = cp[(bang + 1)..].TrimEnd('\\', '/').TrimEnd('/');
                var lastSlash = inner.LastIndexOfAny(new[] { '\\', '/' });
                if (lastSlash < 0)
                {
                    return ArchiveService.Scheme + cp[ArchiveService.Scheme.Length..bang] + "!";
                }
                return ArchiveService.Scheme + cp[ArchiveService.Scheme.Length..bang] + "!" + inner[..lastSlash] + "/";
            }
            var parent = Directory.GetParent(cp.TrimEnd(Path.DirectorySeparatorChar));
            return parent != null ? parent.FullName + Path.DirectorySeparatorChar : cp;
        }

        var resolved = _fileSystem.ResolvePath(path);
        if (resolved.EndsWith(':') && resolved.Length == 2)
        {
            resolved += Path.DirectorySeparatorChar;
        }
        if (!resolved.EndsWith(Path.DirectorySeparatorChar) && resolved.Length > 3)
        {
            resolved += Path.DirectorySeparatorChar;
        }
        return resolved;
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
        if (_backStack.Count == 0) return;
        _forwardStack.Push(CurrentPath);
        CurrentPath = _backStack.Pop();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(CurrentPath);
        CurrentPath = _forwardStack.Pop();
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    /// <summary>Lists the current directory, cancelling any in-flight refresh first.</summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        // Cancel the previous refresh so a slow listing can't overwrite a newer one.
        var previous = _refreshCts;
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }
        var token = cts.Token;

        await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

        try
        {
            IReadOnlyList<FileSystemEntry> entries;
            try
            {
                if (IsArchive)
                {
                    var cp = CurrentPath;
                    if (!cp.EndsWith('/') && !cp.EndsWith('\\')) cp += "/";
                    entries = await _archive.EnumerateAsync(cp, token).ConfigureAwait(false);
                }
                else
                {
                    entries = await _fileSystem.GetDirectoryContentsAsync(CurrentPath, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.WriteLine($"PaneViewModel.RefreshAsync failed for '{CurrentPath}': {ex.Message}");
                entries = Array.Empty<FileSystemEntry>();
            }

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                _allEntries = entries;
                ItemCount = entries.Count;
                ApplyFilter();
                StatusText = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
            });

            // Watch the current directory (no-op for archive paths).
            _watcher.Watch(IsArchive ? string.Empty : CurrentPath, () => _ = RefreshAsync());

            await UpdateGitStatusAsync(token).ConfigureAwait(false);

            if (!token.IsCancellationRequested) Navigated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            // Only the newest refresh clears the spinner, so a cancelled older one
            // can't switch it off while a newer listing is still running.
            if (ReferenceEquals(_refreshCts, cts))
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
            cts.Dispose();
        }
    }

    public void ApplyFilter()
    {
        if (string.IsNullOrEmpty(FilterText))
        {
            _filteredEntries = _allEntries;
        }
        else
        {
            var pool = ArrayPoolList<FileSystemEntry>.Rent(Math.Max(64, _allEntries.Count));
            try
            {
                var filter = FilterText;
                var src = _allEntries;
                for (var i = 0; i < src.Count; i++)
                {
                    var e = src[i];
                    if (e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        pool.Add(e);
                    }
                }
                _filteredEntries = pool.ToReadOnlyAndReset();
            }
            finally
            {
                pool.Dispose();
            }
        }

        _filteredEntries = OrderEntries(_filteredEntries);
        OnPropertyChanged(nameof(FilteredEntries));
    }

    private void ScheduleFilter()
    {
        _filterDebouncer.Schedule(_ =>
        {
            Dispatcher.UIThread.Post(ApplyFilter);
            return Task.CompletedTask;
        });
    }

    private async Task UpdateGitStatusAsync(CancellationToken token)
    {
        try
        {
            var status = await _git.GetStatusAsync(CurrentPath, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() => RepositoryStatus = status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"UpdateGitStatusAsync failed: {ex.Message}");
        }
    }

    /// <summary>Directories first, then the active sort column (asc/desc).</summary>
    private IReadOnlyList<FileSystemEntry> OrderEntries(IReadOnlyList<FileSystemEntry> entries)
    {
        if (entries is not FileSystemEntry[] arr || arr.Length == 0) return entries;
        var col = SortColumn;
        var desc = SortDescending;
        Array.Sort(arr, (a, b) =>
        {
            var c = a.IsDirectory.CompareTo(b.IsDirectory);
            if (c != 0) return -c; // directories first, regardless of sort direction

            var r = col switch
            {
                "Size" => a.Size.CompareTo(b.Size),
                "Modified" => a.Modified.CompareTo(b.Modified),
                "Type" => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            };
            if (r == 0 && col != "Name")
            {
                r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            }
            return desc ? -r : r;
        });
        return arr;
    }

    [RelayCommand]
    private void SortBy(string? column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (SortColumn == column) SortDescending = !SortDescending;
        else { SortColumn = column; SortDescending = false; }
        ApplyFilter();
    }

    [RelayCommand]
    private void Activate(FileSystemEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.Value.FullPath)) return;
        if (entry.Value.IsDirectory || _archive.IsArchive(entry.Value.FullPath))
        {
            NavigateTo(entry.Value.FullPath);
        }
        else
        {
            EntryActivated?.Invoke(this, entry.Value);
        }
    }

    [RelayCommand]
    private void GoUp() => NavigateTo("..");

    [RelayCommand]
    private void Refresh() => _ = RefreshAsync();

    [RelayCommand]
    private void ToggleEditPath() => IsEditingPath = !IsEditingPath;

    [RelayCommand]
    private void OpenInOtherPane(FileSystemEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.Value.FullPath))
        {
            entry = new FileSystemEntry(CurrentPath, string.Empty, true, 0, DateTime.MinValue, string.Empty);
        }
        OpenInOtherPaneRequested?.Invoke(this, entry.Value);
    }

    [RelayCommand]
    private void CopyPath()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
            {
                desk.MainWindow?.Clipboard?.SetTextAsync(CurrentPath);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"CopyPath: {ex.Message}"); }
    }

    // ---- git branch flyout ----

    [RelayCommand]
    private async Task OpenBranchFlyout()
    {
        Branches.Clear();
        var list = await _git.ListBranchesAsync(CurrentPath).ConfigureAwait(true);
        foreach (var b in list) Branches.Add(b);
        IsBranchFlyoutOpen = true;
    }

    [RelayCommand]
    private async Task CheckoutBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch)) return;
        IsBranchFlyoutOpen = false;
        if (await _git.CheckoutBranchAsync(CurrentPath, branch).ConfigureAwait(true))
        {
            await RefreshAsync();
        }
    }

    // ---- drag & drop transfer ----

    /// <summary>Copies (or moves) the given on-disk paths into this pane's current directory.</summary>
    public async Task AcceptDropAsync(IReadOnlyList<string> sourcePaths, bool copy)
    {
        if (IsArchive || string.IsNullOrEmpty(CurrentPath) || sourcePaths.Count == 0) return;
        var destDir = CurrentPath;

        await Task.Run(() =>
        {
            foreach (var src in sourcePaths)
            {
                try
                {
                    if (Directory.Exists(src))
                    {
                        var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        var target = Path.Combine(destDir, name);
                        if (copy) CopyDirectory(src, target);
                        else Directory.Move(src, target);
                    }
                    else if (File.Exists(src))
                    {
                        var target = Path.Combine(destDir, Path.GetFileName(src));
                        if (copy) File.Copy(src, target, overwrite: true);
                        else File.Move(src, target, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AcceptDrop '{src}': {ex.Message}");
                }
            }
        }).ConfigureAwait(false);

        await RefreshAsync();
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    // ---- session persistence ----

    public PaneState CaptureState() => new()
    {
        Path = CurrentPath,
        ViewMode = (int)ViewMode,
        SortColumn = SortColumn,
        SortDescending = SortDescending
    };

    public void RestoreState(PaneState state)
    {
        ViewMode = (LayoutMode)state.ViewMode;
        SortColumn = string.IsNullOrEmpty(state.SortColumn) ? "Name" : state.SortColumn;
        SortDescending = state.SortDescending;
        if (!string.IsNullOrEmpty(state.Path)) NavigateTo(state.Path);
    }

    public void Dispose()
    {
        try { _refreshCts?.Cancel(); } catch (ObjectDisposedException) { }
        _refreshCts?.Dispose();
        _watcher.Dispose();
        _filterDebouncer.Dispose();
        ServiceLocator.Settings.SettingsChanged -= OnSettingsChanged;
    }
}
