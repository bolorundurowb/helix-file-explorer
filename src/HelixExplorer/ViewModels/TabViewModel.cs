using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using System.Diagnostics;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Session;

namespace HelixExplorer.ViewModels;

public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemProvider _fileSystem;
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IOsFileClipboard _osClipboard;
    private readonly IShellContextMenuService _shellContextMenu;
    private readonly IUiHost _uiHost;
    private readonly IGitProvider _git;
    private readonly IArchiveProvider _archive;
    private readonly IFolderColorService _folderColors;
    private readonly Func<IFileChangeWatcher> _watcherFactory;
    private bool _disposed;

    public TabViewModel(
        IFileSystemProvider fileSystem,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        IShellContextMenuService shellContextMenu,
        IUiHost uiHost,
        IGitProvider git,
        IArchiveProvider archive,
        IFolderColorService folderColors,
        Func<IFileChangeWatcher> watcherFactory)
    {
        _fileSystem = fileSystem;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _shellContextMenu = shellContextMenu;
        _uiHost = uiHost;
        _git = git;
        _archive = archive;
        _folderColors = folderColors;
        _watcherFactory = watcherFactory;
        LeftPane = CreatePane();
        _activePane = LeftPane;
        LeftPane.IsActive = true;
        UpdateTitle();
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? Navigated;
    public event EventHandler? StateChanged;

    public PaneViewModel LeftPane { get; }

    [ObservableProperty] private PaneViewModel? _rightPane;

    [ObservableProperty] private PaneViewModel _activePane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSinglePane))]
    [NotifyCanExecuteChangedFor(nameof(SwapPanesCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlipOrientationCommand))]
    private bool _isDualPane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHorizontalSplit))]
    private PaneSplitOrientation _orientation = PaneSplitOrientation.Vertical;

    [ObservableProperty] private string _title = "New Tab";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TintBrush))]
    [NotifyPropertyChangedFor(nameof(HasTint))]
    private Color? _tint;

    public bool IsSinglePane => !IsDualPane;
    public bool IsHorizontalSplit => Orientation == PaneSplitOrientation.Horizontal;
    public bool HasTint => Tint.HasValue;
    public IBrush? TintBrush => Tint is { } c ? new SolidColorBrush(c) : null;

    private PaneViewModel CreatePane()
    {
        var pane = new PaneViewModel(
            _fileSystem,
            _archive,
            _folderColors,
            _fileOps,
            _clipboard,
            _osClipboard,
            _shellContextMenu,
            _uiHost,
            _git,
            _watcherFactory());
        pane.Navigated += OnPaneNavigated;
        pane.EntryActivated += OnEntryActivated;
        pane.OpenInNewTabRequested += OnOpenInNewTabRequested;
        pane.OpenInNewPaneRequested += OnOpenInNewPaneRequested;
        return pane;
    }

    private async void OnEntryActivated(object? sender, FileSystemEntry entry)
    {
        if (sender is not PaneViewModel pane)
            return;

        var pathToOpen = entry.FullPath;
        if (ArchivePath.IsVirtual(entry.FullPath))
        {
            pathToOpen = await _archive.ExtractEntryAsync(entry.FullPath).ConfigureAwait(true);
            if (string.IsNullOrEmpty(pathToOpen))
            {
                pane.StatusText = $"Could not extract {entry.Name}";
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pathToOpen,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open '{entry.FullPath}': {ex.Message}");
            pane.StatusText = $"Could not open {entry.Name}";
        }
    }

    public event EventHandler<string>? OpenInNewTabRequested;

    private void OnOpenInNewTabRequested(object? sender, string path)
        => OpenInNewTabRequested?.Invoke(this, path);

    private void OnOpenInNewPaneRequested(object? sender, string path)
    {
        if (!IsDualPane)
            ToggleDualPane();

        RightPane?.NavigateTo(path);
        if (RightPane is not null)
            ActivePane = RightPane;
    }

    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActivePane))
        {
            UpdateTitle();
            Navigated?.Invoke(this, EventArgs.Empty);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActivePaneChanged(PaneViewModel? oldValue, PaneViewModel newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
            oldValue.IsActive = false;
        newValue.IsActive = true;
        UpdateTitle();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOrientationChanged(PaneSplitOrientation value) => StateChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsDualPaneChanged(bool value) => StateChanged?.Invoke(this, EventArgs.Empty);

    partial void OnTintChanged(Color? value) => StateChanged?.Invoke(this, EventArgs.Empty);

    public void SetActivePane(PaneViewModel pane)
    {
        if (ReferenceEquals(pane, ActivePane))
            return;
        if (ReferenceEquals(pane, LeftPane) || ReferenceEquals(pane, RightPane))
            ActivePane = pane;
    }

    [RelayCommand]
    private void ToggleDualPane()
    {
        if (IsDualPane)
        {
            if (ReferenceEquals(ActivePane, RightPane) && RightPane is not null)
                LeftPane.NavigateTo(RightPane.CurrentPath);

            DisposeRightPane();
            ActivePane = LeftPane;
            IsDualPane = false;
        }
        else
        {
            var right = CreatePane();
            right.IsSelectionActive = LeftPane.IsSelectionActive;
            RightPane = right;
            IsDualPane = true;
            right.RestoreFrom(new PaneSnapshot
            {
                Path = LeftPane.CurrentPath,
                ViewMode = LeftPane.ViewMode,
                SortColumn = LeftPane.SortColumn,
                SortDescending = LeftPane.SortDescending,
                ThumbnailSize = LeftPane.ThumbnailSize
            });
            ActivePane = right;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDualPane))]
    private void SwapPanes()
    {
        if (!IsDualPane || RightPane is null)
            return;

        var leftPath = LeftPane.CurrentPath;
        var rightPath = RightPane.CurrentPath;
        LeftPane.NavigateTo(rightPath);
        RightPane.NavigateTo(leftPath);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsDualPane))]
    private void FlipOrientation()
        => Orientation = Orientation == PaneSplitOrientation.Vertical
            ? PaneSplitOrientation.Horizontal
            : PaneSplitOrientation.Vertical;

    [RelayCommand]
    private void SetTint(string? hex)
        => Tint = string.IsNullOrWhiteSpace(hex) ? null : Color.Parse(hex);

    [RelayCommand]
    private void ClearTint() => Tint = null;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateTitle() => Title = DescribePath(ActivePane.CurrentPath);

    private static string DescribePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "New Tab";

        if (ArchivePath.IsVirtual(path)
            && ArchivePath.TryParse(path, out var archiveFile, out var inner))
        {
            var archiveName = Path.GetFileName(archiveFile);
            if (string.IsNullOrEmpty(inner))
                return archiveName;

            var innerName = inner.TrimEnd('/', '\\');
            var lastSlash = innerName.LastIndexOf('/');
            var leaf = lastSlash < 0 ? innerName : innerName[(lastSlash + 1)..];
            return string.IsNullOrEmpty(leaf) ? archiveName : $"{archiveName}:{leaf}";
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + Path.DirectorySeparatorChar;

        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    public TabSnapshot CreateSnapshot() => new()
    {
        LeftPane = LeftPane.CreateSnapshot(),
        RightPane = RightPane?.CreateSnapshot(),
        IsDualPane = IsDualPane,
        IsRightPaneActive = ReferenceEquals(ActivePane, RightPane),
        Orientation = Orientation,
        TintArgb = Tint?.ToUInt32()
    };

    public void RestoreFrom(TabSnapshot snapshot)
    {
        Tint = snapshot.TintArgb is { } argb ? Color.FromUInt32(argb) : null;
        Orientation = snapshot.Orientation;

        if (snapshot.IsDualPane)
        {
            var right = CreatePane();
            RightPane = right;
            IsDualPane = true;
            right.RestoreFrom(snapshot.RightPane ?? new PaneSnapshot());
            ActivePane = snapshot.IsRightPaneActive ? right : LeftPane;
        }

        LeftPane.RestoreFrom(snapshot.LeftPane);
    }

    private void DisposeRightPane()
    {
        if (RightPane is null)
            return;
        DetachPane(RightPane);
        RightPane.Dispose();
        RightPane = null;
    }

    private void DetachPane(PaneViewModel pane)
    {
        pane.Navigated -= OnPaneNavigated;
        pane.EntryActivated -= OnEntryActivated;
        pane.OpenInNewTabRequested -= OnOpenInNewTabRequested;
        pane.OpenInNewPaneRequested -= OnOpenInNewPaneRequested;
    }

    public void RefreshFolderColorBindings()
    {
        LeftPane.RefreshFolderColorBindings();
        RightPane?.RefreshFolderColorBindings();
    }

    public void SetSelectionActive(bool isActive)
    {
        LeftPane.IsSelectionActive = isActive;
        if (RightPane is not null)
            RightPane.IsSelectionActive = isActive;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DetachPane(LeftPane);
        LeftPane.Dispose();
        DisposeRightPane();
    }
}
