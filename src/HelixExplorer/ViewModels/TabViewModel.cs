using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using System.Diagnostics;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Session;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels;

public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService _clipboard;
    private readonly IArchiveProvider _archive;
    private readonly IPaneViewModelFactory _paneFactory;
    private readonly HomePageViewModel _home;
    private readonly SettingsPageViewModel? _settings;
    private bool _showHiddenFiles;
    private bool _showFileExtensions = true;
    private DirectorySortMode _directorySort = DirectorySortMode.MixedWithFiles;
    private bool _disposed;

    public TabViewModel(
        IClipboardService clipboard,
        IArchiveProvider archive,
        IPaneViewModelFactory paneFactory,
        HomePageViewModel home,
        TabKind kind = TabKind.Browser,
        SettingsPageViewModel? settings = null)
    {
        _clipboard = clipboard;
        _archive = archive;
        _paneFactory = paneFactory;
        _home = home;
        _settings = settings;
        Kind = kind;
        _clipboard.Changed += OnClipboardChanged;
        LeftPane = CreatePane();
        _activePane = LeftPane;
        if (kind == TabKind.Browser)
            LeftPane.IsActive = true;

        UpdateTitle();
    }

    public TabKind Kind { get; }

    public HomePageViewModel Home => _home;

    public SettingsPageViewModel? Settings => _settings;

    public bool IsBrowserTab => Kind == TabKind.Browser;

    public bool IsSettingsTab => Kind == TabKind.Settings;

    public event EventHandler? CloseRequested;
    public event EventHandler? SelectionChanged;

    private void OnPaneSelectionChanged(object? sender, EventArgs e)
        => SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        LeftPane.RefreshCutState();
        RightPane?.RefreshCutState();
    }
    public event EventHandler? Navigated;
    public event EventHandler? SortChanged;
    public event EventHandler? LayoutChanged;

    private void OnPaneSortChanged(object? sender, EventArgs e)
        => SortChanged?.Invoke(this, EventArgs.Empty);

    private void OnPaneLayoutChanged(object? sender, EventArgs e)
        => LayoutChanged?.Invoke(this, EventArgs.Empty);

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

    public bool LeftShowsHome => LeftPane.IsHome;

    public bool ShowsHomeIcon => IsBrowserTab && ActivePane.IsHome;

    public bool ShowsFolderIcon => !IsSettingsTab && !ShowsHomeIcon;

    private PaneViewModel CreatePane()
    {
        var pane = _paneFactory.Create();
        pane.SortChanged += OnPaneSortChanged;
        pane.LayoutChanged += OnPaneLayoutChanged;
        pane.Navigated += OnPaneNavigated;
        pane.EntryActivated += OnEntryActivated;
        pane.OpenInNewTabRequested += OnOpenInNewTabRequested;
        pane.OpenInOtherPaneRequested += OnOpenInOtherPaneRequested;
        pane.PinPathRequested += OnPanePinPathRequested;
        pane.SelectionChanged += OnPaneSelectionChanged;
        pane.ApplyViewSettings(_showHiddenFiles, _showFileExtensions, _directorySort);
        return pane;
    }

    public void ApplyViewSettings(bool showHiddenFiles, bool showFileExtensions, DirectorySortMode directorySort)
    {
        _showHiddenFiles = showHiddenFiles;
        _showFileExtensions = showFileExtensions;
        _directorySort = directorySort;
        LeftPane.ApplyViewSettings(showHiddenFiles, showFileExtensions, directorySort);
        RightPane?.ApplyViewSettings(showHiddenFiles, showFileExtensions, directorySort);
    }

    private async void OnEntryActivated(object? sender, FileSystemEntry entry)
    {
        if (sender is not PaneViewModel pane)
            return;

        try
        {
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
    public event EventHandler<(string Path, bool Pin)>? PinPathRequested;

    private void OnOpenInNewTabRequested(object? sender, string path)
        => OpenInNewTabRequested?.Invoke(this, path);

    private void OnOpenInOtherPaneRequested(object? sender, string path)
    {
        if (!IsDualPane)
            ToggleDualPane();

        var target = ReferenceEquals(ActivePane, LeftPane) ? RightPane : LeftPane;
        target ??= RightPane ?? LeftPane;
        target.NavigateTo(path);
        ActivePane = target;
    }

    private void OnPanePinPathRequested(object? sender, (string Path, bool Pin) args)
        => PinPathRequested?.Invoke(this, args);

    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        // Dual-pane titles need either pane; single-pane chrome still keys off ActivePane.
        UpdateTitle();
        if (ReferenceEquals(sender, ActivePane))
            Navigated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActivePaneChanged(PaneViewModel? oldValue, PaneViewModel newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
            oldValue.IsActive = false;
        newValue.IsActive = true;
        UpdateTitle();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOrientationChanged(PaneSplitOrientation value) { }

    partial void OnRightPaneChanged(PaneViewModel? oldValue, PaneViewModel? newValue)
    {
    }

    partial void OnIsDualPaneChanged(bool value) => UpdateTitle();

    partial void OnTintChanged(Color? value) { }

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
                DirectorySort = LeftPane.DirectorySort,
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

    private void UpdateTitle()
    {
        if (IsSettingsTab)
        {
            Title = "Settings";
            NotifyTabIconPropertiesChanged();
            return;
        }

        if (IsDualPane && RightPane is not null)
        {
            Title = $"{DescribePath(LeftPane.CurrentPath)} | {DescribePath(RightPane.CurrentPath)}";
            NotifyTabIconPropertiesChanged();
            return;
        }

        if (ActivePane.IsHome)
        {
            Title = "Home";
            NotifyTabIconPropertiesChanged();
            return;
        }

        Title = DescribePath(ActivePane.CurrentPath);
        NotifyTabIconPropertiesChanged();
    }

    private void NotifyTabIconPropertiesChanged()
    {
        OnPropertyChanged(nameof(ShowsHomeIcon));
        OnPropertyChanged(nameof(ShowsFolderIcon));
    }

    private static string DescribePath(string path)
    {
        if (string.Equals(path, PaneConstants.HomeRoute, StringComparison.Ordinal))
            return "Home";

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
        pane.SortChanged -= OnPaneSortChanged;
        pane.LayoutChanged -= OnPaneLayoutChanged;
        pane.Navigated -= OnPaneNavigated;
        pane.EntryActivated -= OnEntryActivated;
        pane.OpenInNewTabRequested -= OnOpenInNewTabRequested;
        pane.OpenInOtherPaneRequested -= OnOpenInOtherPaneRequested;
        pane.PinPathRequested -= OnPanePinPathRequested;
        pane.SelectionChanged -= OnPaneSelectionChanged;
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
        _clipboard.Changed -= OnClipboardChanged;
        DetachPane(LeftPane);
        LeftPane.Dispose();
        DisposeRightPane();
    }
}
