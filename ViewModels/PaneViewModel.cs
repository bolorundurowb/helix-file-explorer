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

/// <summary>State for one file pane (left or right).</summary>
public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    private readonly FileChangeWatcherService _watcher;
    private readonly IFileSystemService _fileSystem;
    private readonly IArchiveService _archive;
    private readonly IGitService _git;
    private readonly AsyncDebouncer _filterDebouncer = new(TimeSpan.FromMilliseconds(150));
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private IReadOnlyList<FileSystemEntry> _allEntries = Array.Empty<FileSystemEntry>();
    private IReadOnlyList<FileSystemEntry> _filteredEntries = Array.Empty<FileSystemEntry>();
    private FileSystemEntry? _selected;

    public PaneViewModel(
        FileChangeWatcherService watcher,
        IFileSystemService fileSystem,
        IArchiveService archive,
        IGitService git)
    {
        _watcher = watcher;
        _fileSystem = fileSystem;
        _archive = archive;
        _git = git;
        ServiceLocator.Settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(FilteredEntries));

    /// <summary>Raised when the user activates an entry (double-click or Enter).</summary>
    public event EventHandler<FileSystemEntry>? EntryActivated;

    /// <summary>Fired when navigation completes — used by Tab/StatusBar to refresh git info.</summary>
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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    public IReadOnlyList<FileSystemEntry> FilteredEntries => _filteredEntries;

    public FileSystemEntry? SelectedEntry
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    partial void OnFilterTextChanged(string value) => ScheduleFilter();

    partial void OnCurrentPathChanged(string value)
    {
        IsArchive = value.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase);
        _ = RefreshAsync();
    }

    /// <summary>Drives path semantics; resolves .., symlinks, and archive:// schemes.</summary>
    public void NavigateTo(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string resolved = ResolveDestination(path);
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
            string cp = CurrentPath;
            if (cp.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                int bang = cp.IndexOf('!');
                if (bang < 0)
                {
                    // archive://X! → leave the archive.
                    return cp[ArchiveService.Scheme.Length..];
                }
                string inner = cp[(bang + 1)..].TrimEnd('\\', '/').TrimEnd('/');
                int lastSlash = inner.LastIndexOfAny(new[] { '\\', '/' });
                if (lastSlash < 0)
                {
                    return ArchiveService.Scheme + cp[ArchiveService.Scheme.Length..bang] + "!";
                }
                return ArchiveService.Scheme + cp[ArchiveService.Scheme.Length..bang] + "!" + inner[..lastSlash] + "/";
            }
            var parent = Directory.GetParent(cp.TrimEnd(Path.DirectorySeparatorChar));
            return parent != null ? parent.FullName + Path.DirectorySeparatorChar : cp;
        }

        string resolved = _fileSystem.ResolvePath(path);
        if (resolved.EndsWith(":") && resolved.Length == 2)
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

    /// <summary>Lists the current directory, applying filter on completion.</summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        IsLoading = true;
        CancellationTokenSource? ours = new();
        try
        {
            CancellationToken token = ours.Token;

            IReadOnlyList<FileSystemEntry> entries;
            try
            {
                if (IsArchive)
                {
                    string cp = CurrentPath;
                    if (!cp.EndsWith('/') && !cp.EndsWith('\\'))
                    {
                        cp += "/";
                    }
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
                _allEntries = entries;
                ItemCount = entries.Count;
                ApplyFilter();
                StatusText = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
                IsLoading = false;
            }, DispatcherPriority.Background);

            // Watch the current directory (no-op for archive paths).
            if (!IsArchive)
            {
                _watcher.Watch(CurrentPath, () => _ = RefreshAsync());
            }
            else
            {
                _watcher.Stop();
            }

            // Refresh git status off-thread.
            await UpdateGitStatusAsync(token).ConfigureAwait(false);

            Navigated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            ours.Dispose();
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
                string filter = FilterText;
                var src = _allEntries;
                for (int i = 0; i < src.Count; i++)
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

        // Stably sort: directories first, then name.
        var ordered = OrderEntries(_filteredEntries);
        _filteredEntries = ordered;
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
            var status = await _git.GetStatusAsync(CurrentPath, token);
            await Dispatcher.UIThread.InvokeAsync(() => RepositoryStatus = status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"UpdateGitStatusAsync failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<FileSystemEntry> OrderEntries(IReadOnlyList<FileSystemEntry> entries)
    {
        if (entries is not FileSystemEntry[] arr || arr.Length == 0) return entries;
        Array.Sort(arr, static (a, b) =>
        {
            int c = a.IsDirectory.CompareTo(b.IsDirectory);
            if (c != 0) return -c; // directories first
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return arr;
    }

    [RelayCommand]
    private void Activate(FileSystemEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.Value.FullPath)) return;
        if (entry.Value.IsDirectory)
        {
            NavigateTo(entry.Value.FullPath);
        }
        else if (_archive.IsArchive(entry.Value.FullPath))
        {
            NavigateTo(entry.Value.FullPath);
        }
        else
        {
            EntryActivated?.Invoke(this, entry.Value);
        }
    }

    [RelayCommand]
    private void GoUp()
    {
        NavigateTo("..");
    }

    [RelayCommand]
    private void Refresh()
    {
        _ = RefreshAsync();
    }

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
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
            {
                desk.MainWindow?.Clipboard?.SetTextAsync(CurrentPath);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"CopyPath: {ex.Message}"); }
    }

    public void Dispose()
    {
        _filterDebouncer.Dispose();
        ServiceLocator.Settings.SettingsChanged -= OnSettingsChanged;
    }
}