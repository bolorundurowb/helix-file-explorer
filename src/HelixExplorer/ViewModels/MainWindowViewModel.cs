using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.Input;
using HelixExplorer.Localization;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _themeService;
    private readonly IAccentBrushService _accentBrushes;
    private readonly IQuickAccessProvider _quickAccess;
    private readonly INetworkLocationProvider _networkLocations;
    private readonly INetworkDiscoveryAvailability _networkAvailability;
    private readonly IClipboardService _clipboard;
    private readonly IArchiveProvider _archive;
    private readonly IFolderColorService _folderColors;
    private readonly IPaneViewModelFactory _paneFactory;
    private readonly AppSettingsCoordinator _settingsCoordinator;
    private readonly SidebarViewModel _sidebar;
    private readonly CommandPaletteService _commandPalette;
    private readonly TabSessionCoordinator _tabSession;
    private readonly FileOperationReporter _operationReporter;
    private readonly IUserDialogService _dialogs;
    private readonly HomePageViewModel _homePage;
    private readonly SettingsPageViewModel _settingsPage;
    private readonly string _homePath;
    private readonly List<string> _recentPaths = new();
    private IReadOnlyList<NetworkLocationInfo> _lastNetworkLocations = [];
    private bool _networkBrowsingVerified;
    private CancellationTokenSource? _networkCts;
    private bool _disposed;
    private TabViewModel? _lastBrowserTab;
    private bool _restoreWindowLayout;

    private const double MinWindowWidth = 800;
    private const double MinWindowHeight = 500;

    public MainWindowViewModel(
        IThemeService themeService,
        IAccentBrushService accentBrushes,
        IQuickAccessProvider quickAccess,
        INetworkLocationProvider networkLocations,
        INetworkDiscoveryAvailability networkAvailability,
        IClipboardService clipboard,
        IArchiveProvider archive,
        IFolderColorService folderColors,
        IPaneViewModelFactory paneFactory,
        AppSettingsCoordinator settingsCoordinator,
        SidebarViewModel sidebar,
        CommandPaletteService commandPalette,
        TabSessionCoordinator tabSession,
        FileOperationReporter operationReporter,
        IUserDialogService dialogs,
        HomePageViewModel homePage)
    {
        _themeService = themeService;
        _accentBrushes = accentBrushes;
        _quickAccess = quickAccess;
        _networkLocations = networkLocations;
        _networkAvailability = networkAvailability;
        _clipboard = clipboard;
        _archive = archive;
        _folderColors = folderColors;
        _paneFactory = paneFactory;
        _settingsCoordinator = settingsCoordinator;
        _sidebar = sidebar;
        _commandPalette = commandPalette;
        _tabSession = tabSession;
        _operationReporter = operationReporter;
        _dialogs = dialogs;
        OperationReporter = operationReporter;
        _homePage = homePage;
        _settingsPage = new SettingsPageViewModel(this);
        _homePage.NavigateRequested += OnHomeNavigateRequested;
        _operationReporter.PropertyChanged += OnOperationReporterPropertyChanged;

        _homePath = _quickAccess.GetPath(KnownFolderKind.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = GetSettings();
        SidebarWidth = Math.Clamp(settings.SidebarWidth, 200, 450);
        ShowHiddenFiles = settings.ShowHiddenFiles;
        ShowFileExtensions = settings.ShowFileExtensions;
        DirectorySort = settings.DirectorySort;
        Theme = settings.Theme;
        UiFont = settings.UiFont;
        SizeDisplay = settings.SizeDisplay;
        DefaultViewMode = settings.DefaultViewMode;
        DefaultThumbnailSize = Math.Clamp(settings.DefaultThumbnailSize, PaneViewModel.MinThumbnailSize, PaneViewModel.MaxThumbnailSize);
        DefaultDualPane = settings.DefaultDualPane;
        DefaultSplitOrientation = settings.DefaultSplitOrientation;
        AccentColorArgb = settings.AccentColorArgb;
        ApplyOpenInTerminalGesture(settings.OpenInTerminalGesture);
        AutoCheckForUpdates = settings.AutoCheckForUpdates;

        _accentBrushes.ApplyCustomAccent(AccentColorArgb);
        _themeService.ThemeChanged += OnThemeServiceChanged;

        _sidebar.Rebuild(settings.PinnedPaths, settings.UnpinnedPaths);
        _networkAvailability.AvailabilityChanged += OnNetworkAvailabilityChanged;
        _ = RefreshNetworkLocationsAsync();

        _folderColors.ColorsChanged += OnFolderColorsChanged;
    }

    public const string AppDefaultTerminalGesture = "Ctrl+OemTilde";

    /// <summary>
    /// Bound by <see cref="Views.MainWindow"/> to a rebuildable <see cref="KeyBinding"/>.
    /// Empty or invalid values fall back to <see cref="AppDefaultTerminalGesture"/>.
    /// </summary>
    [ObservableProperty]
    private string _openInTerminalGesture = AppDefaultTerminalGesture;

    partial void OnOpenInTerminalGestureChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidKeyGesture(value))
            OpenInTerminalGesture = AppDefaultTerminalGesture;

        PersistChromeSettings();
    }

    private static bool IsValidKeyGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        try
        {
            return KeyGesture.Parse(gesture) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applied at construction before the property setter runs (setter would persist immediately).
    /// </summary>
    private void ApplyOpenInTerminalGesture(string? persisted)
    {
        OpenInTerminalGesture = string.IsNullOrWhiteSpace(persisted) || !IsValidKeyGesture(persisted)
            ? AppDefaultTerminalGesture
            : persisted!;
    }

    [ObservableProperty]
    private bool _autoCheckForUpdates = true;

    partial void OnAutoCheckForUpdatesChanged(bool value) => PersistChromeSettings();

    /// <summary>
    /// Proxies the active pane so one window-level <see cref="KeyBinding"/> works regardless of focus.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenInTerminalFromActivePane))]
    private void OpenInTerminal()
    {
        if (ActivePane is { } pane && pane.OpenInTerminalCommand.CanExecute(null))
            pane.OpenInTerminalCommand.Execute(null);
    }

    private bool CanOpenInTerminalFromActivePane()
    {
        if (IsCommandPaletteOpen)
            return false;

        return ActivePane is { } pane
            && pane.OpenInTerminalCommand.CanExecute(null);
    }

    public void InitializeWindow(bool restoreSession, string? initialPath = null)
    {
        _restoreWindowLayout = restoreSession;

        if (restoreSession)
        {
            RestoreSession();
        }
        else if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var tab = CreateDefaultTab();
            tab.LeftPane.NavigateTo(initialPath);
            AddTab(tab);
            SelectedTab = tab;
        }
        else
        {
            AddTab(CreateDefaultTab());
            SelectedTab = Tabs[0];
        }

        _ = CheckForUpdatesInBackgroundAsync();
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!AutoCheckForUpdates)
            return;

        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        if (!AutoCheckForUpdates || _settingsPage.IsCheckingForUpdates)
            return;

        var command = _settingsPage.CheckForUpdatesCommand;
        if (command.CanExecute(null))
            command.Execute(null);

        while (_settingsPage.IsCheckingForUpdates)
            await Task.Delay(200).ConfigureAwait(true);

        if (_settingsPage.HasUpdate && _settingsPage.UpdateReleaseUrl is { Length: > 0 } url)
        {
            var wantsDownload = await _dialogs.ConfirmAsync(
                "Update Available",
                $"A new version of Helix Explorer is available.\n\n{_settingsPage.UpdateStatus}\n\nWould you like to download it now?");
            if (wantsDownload)
                OpenUrl(url);
        }
    }

    public bool ShouldRestoreWindowLayout => _restoreWindowLayout;

    public void ApplyWindowLayout(Avalonia.Controls.Window window)
    {
        if (!_restoreWindowLayout)
            return;

        var settings = GetSettings();
        if (settings.WindowWidth is not > 0 || settings.WindowHeight is not > 0)
            return;

        window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
        window.Width = Math.Max(MinWindowWidth, settings.WindowWidth.Value);
        window.Height = Math.Max(MinWindowHeight, settings.WindowHeight.Value);

        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
            window.Position = new Avalonia.PixelPoint(settings.WindowX.Value, settings.WindowY.Value);

        if (settings.WindowMaximized)
            window.WindowState = Avalonia.Controls.WindowState.Maximized;
    }

    public void CaptureWindowLayout(Avalonia.Controls.Window window)
    {
        if (!_restoreWindowLayout)
            return;

        var settings = GetSettings();
        settings.WindowMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized;

        if (window.WindowState == Avalonia.Controls.WindowState.Normal)
        {
            settings.WindowWidth = Math.Max(MinWindowWidth, window.Width);
            settings.WindowHeight = Math.Max(MinWindowHeight, window.Height);
            settings.WindowX = window.Position.X;
            settings.WindowY = window.Position.Y;
        }

        settings.SidebarWidth = SidebarWidth;
        SaveSettings(settings);
    }

    public void SyncSidebarWidth(double width)
    {
        var clamped = Math.Clamp(width, 200, 450);
        if (Math.Abs(clamped - SidebarWidth) <= double.Epsilon)
            return;

        SidebarWidth = clamped;
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public ObservableCollection<SidebarItemViewModel> SidebarItems => _sidebar.Items;

    public FileOperationReporter OperationReporter { get; }

    public bool IsDualPaneActive => SelectedTab?.IsDualPane == true;

    public ObservableCollection<CommandItem> FilteredCommands { get; } = new();

    [ObservableProperty] private bool _isCommandPaletteOpen;

    [ObservableProperty] private string _commandPaletteQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetworkBannerVisible))]
    private bool _isDiscoveringNetwork;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetworkBannerVisible))]
    private bool _hasNetworkNotice;

    [ObservableProperty]
    private string _networkBannerText = UiStrings.NetworkDiscoveryBanner;

    public bool IsNetworkBannerVisible => IsDiscoveringNetwork || HasNetworkNotice;

    private async Task RefreshNetworkLocationsAsync()
    {
        _networkCts?.Cancel();
        _networkCts = new CancellationTokenSource();
        var ct = _networkCts.Token;

        IsDiscoveringNetwork = true;
        HasNetworkNotice = false;
        NetworkBannerText = UiStrings.NetworkDiscoveryBanner;
        try
        {
            _networkAvailability.Refresh();
            var result = await _networkLocations.GetNetworkLocationsAsync(ct).ConfigureAwait(true);
            if (!ct.IsCancellationRequested)
            {
                _lastNetworkLocations = result.Locations;
                if (result.Status == NetworkDiscoveryStatus.Discovered)
                    _networkBrowsingVerified = true;

                RebuildSidebar();
                UpdateNetworkNotice();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsDiscoveringNetwork = false;
                RefreshHomeDashboard();
            }
        }
    }

    private void UpdateNetworkNotice()
    {
        if (!NetworkNoticePolicy.ShouldShowUnavailableNotice(
                _networkBrowsingVerified,
                _lastNetworkLocations.Count > 0,
                _networkAvailability.IsUnavailable))
        {
            HasNetworkNotice = false;
            return;
        }

        NetworkBannerText = UiStrings.NetworkDiscoveryFailed;
        HasNetworkNotice = true;
    }

    private void OnNetworkAvailabilityChanged(object? sender, EventArgs e) => UpdateNetworkNotice();

    private void NoteNetworkBrowsing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!NetworkPath.IsUnc(path) && !NetworkPath.IsNetworkRoot(path))
            return;

        _networkBrowsingVerified = true;
        HasNetworkNotice = false;
    }

    private void RebuildSidebar()
    {
        var settings = GetSettings();
        _sidebar.Rebuild(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            _lastNetworkLocations,
            ActivePane?.CurrentPath);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePane))]
    [NotifyPropertyChangedFor(nameof(HasMultipleTabs))]
    [NotifyPropertyChangedFor(nameof(IsBrowserTab))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTab))]
    [NotifyPropertyChangedFor(nameof(ShowFileToolbar))]
    [NotifyPropertyChangedFor(nameof(ShowBrowserChrome))]
    private TabViewModel? _selectedTab;

    [ObservableProperty] private bool _isStatusCentreOpen;

    public bool IsBrowserTab => SelectedTab?.IsBrowserTab ?? true;

    public bool IsSettingsTab => SelectedTab?.IsSettingsTab ?? false;

    public bool ShowFileToolbar => IsBrowserTab && ActivePane?.IsHome != true;

    public bool ShowBrowserChrome => IsBrowserTab;

    [ObservableProperty]
    private string _title = "Helix Explorer";

    [ObservableProperty]
    private double _sidebarWidth = 300;

    [ObservableProperty]
    private LayoutMode _defaultViewMode = LayoutMode.Details;

    [ObservableProperty]
    private double _defaultThumbnailSize = 72;

    [ObservableProperty]
    private bool _defaultDualPane;

    [ObservableProperty]
    private PaneSplitOrientation _defaultSplitOrientation = PaneSplitOrientation.Vertical;

    [ObservableProperty]
    private uint? _accentColorArgb;

    [ObservableProperty]
    private bool _isWindowActive = true;

    [ObservableProperty]
    private bool _showHiddenFiles;

    [ObservableProperty]
    private bool _showFileExtensions = true;

    [ObservableProperty]
    private DirectorySortMode _directorySort = DirectorySortMode.MixedWithFiles;

    [ObservableProperty]
    private ThemeMode _theme = ThemeMode.System;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedUiFontOption))]
    private UiFontFamily _uiFont = UiFontFamily.System;

    public UiFontOption SelectedUiFontOption
    {
        get => UiFontCatalog.Options.First(option => option.Value == UiFont);
        set
        {
            if (value is null || value.Value == UiFont)
                return;

            UiFont = value.Value;
        }
    }

    [ObservableProperty]
    private SizeDisplayMode _sizeDisplay = SizeDisplayMode.Binary;

    public event Action<SizeDisplayMode>? SizeDisplayChanged;

    partial void OnShowHiddenFilesChanged(bool value)
    {
        PersistViewSettings();
        ApplyViewSettingsToTabs();
    }

    partial void OnShowFileExtensionsChanged(bool value)
    {
        PersistViewSettings();
        ApplyViewSettingsToTabs();
    }

    partial void OnDirectorySortChanged(DirectorySortMode value)
    {
        OnPropertyChanged(nameof(FoldersFirst));
        PersistViewSettings();
        ApplyViewSettingsToTabs();
    }

    public bool FoldersFirst
    {
        get => DirectorySort == DirectorySortMode.FoldersFirst;
        set => DirectorySort = value ? DirectorySortMode.FoldersFirst : DirectorySortMode.MixedWithFiles;
    }

    partial void OnThemeChanged(ThemeMode value) => PersistChromeSettings();

    partial void OnUiFontChanged(UiFontFamily value)
    {
        PersistChromeSettings();
        OnPropertyChanged(nameof(SelectedUiFontOption));
    }

    partial void OnSizeDisplayChanged(SizeDisplayMode value)
    {
        PersistChromeSettings();
        SizeDisplayChanged?.Invoke(value);
        RefreshSizeDisplayOnAllPanes();
    }

    partial void OnSidebarWidthChanged(double value)
    {
        var clamped = Math.Clamp(value, 200, 450);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            SidebarWidth = clamped;
            return;
        }

        PersistChromeSettings();
    }

    partial void OnDefaultViewModeChanged(LayoutMode value) => PersistChromeSettings();

    partial void OnDefaultThumbnailSizeChanged(double value)
    {
        var clamped = Math.Clamp(value, PaneViewModel.MinThumbnailSize, PaneViewModel.MaxThumbnailSize);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            DefaultThumbnailSize = clamped;
            return;
        }

        PersistChromeSettings();
    }

    partial void OnDefaultDualPaneChanged(bool value) => PersistChromeSettings();

    partial void OnDefaultSplitOrientationChanged(PaneSplitOrientation value) => PersistChromeSettings();

    partial void OnAccentColorArgbChanged(uint? value) => PersistChromeSettings();

    private void OnThemeServiceChanged(ThemeMode _)
        => _accentBrushes.ApplyCustomAccent(AccentColorArgb);

    private void PersistViewSettings()
    {
        _settingsCoordinator.ScheduleSave(settings =>
        {
            settings.ShowHiddenFiles = ShowHiddenFiles;
            settings.ShowFileExtensions = ShowFileExtensions;
            settings.DirectorySort = DirectorySort;
        });
    }

    private void PersistChromeSettings()
    {
        _settingsCoordinator.ScheduleSave(settings =>
        {
            settings.SidebarWidth = SidebarWidth;
            settings.Theme = Theme;
            settings.UiFont = UiFont;
            settings.SizeDisplay = SizeDisplay;
            settings.DefaultViewMode = DefaultViewMode;
            settings.DefaultThumbnailSize = DefaultThumbnailSize;
            settings.DefaultDualPane = DefaultDualPane;
            settings.DefaultSplitOrientation = DefaultSplitOrientation;
            settings.AccentColorArgb = AccentColorArgb;
            settings.OpenInTerminalGesture = string.IsNullOrWhiteSpace(OpenInTerminalGesture)
                ? AppDefaultTerminalGesture
                : OpenInTerminalGesture;
            settings.AutoCheckForUpdates = AutoCheckForUpdates;
        });
    }

    private AppSettings GetSettings() => _settingsCoordinator.Load();

    private void SaveSettings(AppSettings settings)
    {
        // Callers mutate the cached instance from GetSettings(); flush writes it immediately.
        _ = settings;
        _settingsCoordinator.Flush();
    }

    private void FlushSettings() => _settingsCoordinator.Flush();

    private void ApplyViewSettingsToTabs()
    {
        foreach (var tab in Tabs)
            tab.ApplyViewSettings(ShowHiddenFiles, ShowFileExtensions, DirectorySort);
    }

    private void RefreshSizeDisplayOnAllPanes()
    {
        foreach (var tab in Tabs)
        {
            tab.LeftPane.RefreshListingPresentation();
            tab.RightPane?.RefreshListingPresentation();
        }
    }

    public PaneViewModel? ActivePane => SelectedTab?.ActivePane;

    public bool HasMultipleTabs => Tabs.Count > 1;

    private void RestoreSession()
    {
        _tabSession.Restore(
            _recentPaths,
            () => CreateTab(),
            CreateDefaultTab,
            AddTab,
            tab => SelectedTab = tab,
            Tabs);
    }

    public void SaveSession()
        => _tabSession.Save(Tabs, SelectedTab, _recentPaths);

    private TabViewModel CreateTab(TabKind kind = TabKind.Browser)
    {
        var tab = new TabViewModel(
            _clipboard,
            _archive,
            _paneFactory,
            _homePage,
            kind,
            kind == TabKind.Settings ? _settingsPage : null);
        tab.CloseRequested += OnTabCloseRequested;
        tab.SortChanged += OnTabSortChanged;
        tab.LayoutChanged += OnTabLayoutChanged;
        tab.Navigated += OnTabNavigated;
        tab.OpenInNewTabRequested += OnOpenInNewTabRequested;
        tab.PinPathRequested += OnPinPathRequested;
        tab.SelectionChanged += OnTabSelectionChanged;
        tab.SetSelectionActive(IsWindowActive);
        ApplyDefaultTabLayout(tab);
        return tab;
    }

    private void ApplyDefaultTabLayout(TabViewModel tab)
    {
        tab.LeftPane.SetViewMode(DefaultViewMode);
        tab.LeftPane.ThumbnailSize = DefaultThumbnailSize;
        if (DefaultDualPane && !tab.IsDualPane)
            tab.ToggleDualPaneCommand.Execute(null);
    }

    partial void OnIsWindowActiveChanged(bool value)
    {
        if (value)
        {
            _networkAvailability.Refresh();
            UpdateNetworkNotice();
        }

        foreach (var tab in Tabs)
            tab.SetSelectionActive(value);
    }

    private void OnOpenInNewTabRequested(object? sender, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var tab = CreateTab();
        tab.LeftPane.NavigateTo(path);
        AddTab(tab);
        SelectedTab = tab;
    }

    private TabViewModel CreateDefaultTab()
    {
        var tab = CreateTab();
        tab.LeftPane.NavigateToHome();
        return tab;
    }

    private void AddTab(TabViewModel tab)
    {
        tab.ApplyViewSettings(ShowHiddenFiles, ShowFileExtensions, DirectorySort);
        Tabs.Add(tab);
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    [RelayCommand]
    private void NewTab()
    {
        var tab = CreateDefaultTab();
        AddTab(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        tab ??= SelectedTab;
        SelectedTab = _tabSession.CloseTab(
            Tabs,
            tab,
            SelectedTab,
            DetachTab,
            CreateDefaultTab,
            AddTab);
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private void DetachTab(TabViewModel tab)
    {
        tab.CloseRequested -= OnTabCloseRequested;
        tab.SortChanged -= OnTabSortChanged;
        tab.LayoutChanged -= OnTabLayoutChanged;
        tab.Navigated -= OnTabNavigated;
        tab.OpenInNewTabRequested -= OnOpenInNewTabRequested;
        tab.PinPathRequested -= OnPinPathRequested;
        tab.SelectionChanged -= OnTabSelectionChanged;
    }

    [RelayCommand]
    private void CloseSelectedTab() => CloseTab(SelectedTab);

    [RelayCommand]
    private void SelectNextTab() => CycleSelectedTab(1);

    [RelayCommand]
    private void SelectPreviousTab() => CycleSelectedTab(-1);

    public void CycleSelectedTab(int delta)
        => SelectedTab = _tabSession.CycleSelectedTab(Tabs, SelectedTab, delta);

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab)
            CloseTab(tab);
    }

    private void OnTabSortChanged(object? sender, EventArgs e) => NotifySortChrome();

    private void OnTabLayoutChanged(object? sender, EventArgs e) => NotifyLayoutChrome();

    private void OnTabNavigated(object? sender, EventArgs e)
    {
        if (sender is not TabViewModel tab || !ReferenceEquals(tab, SelectedTab))
            return;

        NoteNetworkBrowsing(tab.ActivePane?.CurrentPath);
        NoteNetworkBrowsing(tab.LeftPane.CurrentPath);
        if (tab.IsDualPane)
            NoteNetworkBrowsing(tab.RightPane?.CurrentPath);

        RecordRecent(tab.ActivePane?.CurrentPath);
        SyncChromeToActivePane();
    }

    private void OnTabSelectionChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTab))
            return;

        NotifyGlobalFileCommandsCanExecuteChanged();
    }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        if (value?.IsBrowserTab == true)
            _lastBrowserTab = value;

        SyncChromeToActivePane();
        NotifyGlobalFileCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(IsDualPaneActive));
    }

    partial void OnCommandPaletteQueryChanged(string value) => RefreshCommandPalette(value);

    private void RefreshCommandPalette(string query)
        => _commandPalette.FilterInto(FilteredCommands, query, _recentPaths);

    private void RecordRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.Equals(path, PaneConstants.HomeRoute, StringComparison.Ordinal))
            return;

        _recentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentPaths.Insert(0, path);
        if (_recentPaths.Count > 12)
            _recentPaths.RemoveAt(_recentPaths.Count - 1);
    }

    private TabViewModel GetOrCreateBrowserTab()
    {
        if (SelectedTab?.IsBrowserTab == true)
            return SelectedTab;

        if (_lastBrowserTab is not null && Tabs.Contains(_lastBrowserTab))
        {
            SelectedTab = _lastBrowserTab;
            return _lastBrowserTab;
        }

        var existing = Tabs.FirstOrDefault(t => t.IsBrowserTab);
        if (existing is not null)
        {
            SelectedTab = existing;
            return existing;
        }

        var tab = CreateDefaultTab();
        AddTab(tab);
        SelectedTab = tab;
        return tab;
    }

    private void NavigateActivePane(Action<PaneViewModel> navigate)
    {
        navigate(GetOrCreateBrowserTab().ActivePane);
    }

    public void NavigateActive(string path) => NavigateActivePane(pane => pane.NavigateTo(path));

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (!IsCommandPaletteOpen)
            return;

        IsStatusCentreOpen = false;
        foreach (var tab in Tabs.Where(t => t.IsBrowserTab))
            RecordRecent(tab.ActivePane?.CurrentPath);

        CommandPaletteQuery = string.Empty;
        RefreshCommandPalette(string.Empty);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsCommandPaletteOpen = false;
        var existing = Tabs.FirstOrDefault(t => t.IsSettingsTab);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = CreateTab(TabKind.Settings);
        AddTab(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        var settingsTab = SelectedTab?.IsSettingsTab == true
            ? SelectedTab
            : Tabs.FirstOrDefault(t => t.IsSettingsTab);

        if (settingsTab is not null)
            CloseTab(settingsTab);
    }

    [RelayCommand]
    private void ToggleStatusCentre() => IsStatusCentreOpen = !IsStatusCentreOpen;

    private void OnOperationReporterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        // Auto-open the status centre the moment an operation starts, so the user sees progress
        // without having to click the status button. Keep it open after completion so the green
        // check / failure row is visible; the user dismisses it via the Close/Clear buttons.
        if (e.PropertyName == nameof(FileOperationReporter.HasActive)
            && _operationReporter.HasActive
            && !IsCommandPaletteOpen)
        {
            IsStatusCentreOpen = true;
        }
    }

    [RelayCommand]
    private void ExecuteCommand(CommandItem? command)
    {
        if (command?.Execute is null)
            return;

        command.Execute(this);
        IsCommandPaletteOpen = false;
    }

    public void SetSidebarFolderColor(SidebarItemViewModel item, string hex)
    {
        if (string.IsNullOrEmpty(item.Path))
            return;

        _folderColors.SetColor(item.Path, ParseColorHex(hex));
    }

    public void ClearSidebarFolderColor(SidebarItemViewModel item)
    {
        if (string.IsNullOrEmpty(item.Path))
            return;

        _folderColors.RemoveColor(item.Path);
    }

    private void OnFolderColorsChanged(object? sender, EventArgs e)
    {
        _sidebar.NotifyFolderColorsChanged();

        foreach (var tab in Tabs)
            tab.RefreshFolderColorBindings();
    }

    private static uint ParseColorHex(string hex)
    {
        var color = Avalonia.Media.Color.Parse(hex);
        return color.ToUInt32();
    }

    private void SyncChromeToActivePane()
    {
        var path = SelectedTab?.ActivePane?.CurrentPath ?? string.Empty;
        UpdateSidebarSelection(path);
        RefreshHomeDashboard();
        Title = SelectedTab is null || string.IsNullOrEmpty(SelectedTab.Title)
            ? "Helix Explorer"
            : $"{SelectedTab.Title} - Helix Explorer";
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(IsDualPaneActive));
        OnPropertyChanged(nameof(IsBrowserTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(ShowFileToolbar));
        OnPropertyChanged(nameof(ShowBrowserChrome));
        OnPropertyChanged(nameof(IsRecycleBinActive));
        NotifyGlobalFileCommandsCanExecuteChanged();
        NotifySortChrome();
        NotifyLayoutChrome();
    }

    public bool IsRecycleBinActive => ActivePane?.IsRecycleBin == true;

    public bool IsDetailsViewActive => ActivePane?.IsDetailsView == true;
    public bool IsListViewActive => ActivePane?.IsListView == true;
    public bool IsGridViewActive => ActivePane?.IsGridView == true;
    public bool IsMillerViewActive => ActivePane?.IsMillerView == true;

    public double ActiveThumbnailSize
    {
        get => ActivePane?.ThumbnailSize ?? DefaultThumbnailSize;
        set
        {
            if (ActivePane is null)
                return;

            ActivePane.ThumbnailSize = value;
            OnPropertyChanged();
        }
    }

    public bool IsSortByName => ActivePane?.IsSortByName == true;
    public bool IsSortByDate => ActivePane?.IsSortByDate == true;
    public bool IsSortByType => ActivePane?.IsSortByType == true;
    public bool IsSortBySize => ActivePane?.IsSortBySize == true;
    public bool IsSortAscending => ActivePane?.IsSortAscending == true;
    public bool IsSortDescending => ActivePane?.IsSortDescendingActive == true;
    public bool IsFoldersFirst => ActivePane?.IsFoldersFirst == true;
    public bool IsFilesFirst => ActivePane?.IsFilesFirst == true;
    public bool IsMixedFolderSort => ActivePane?.IsMixedFolderSort == true;

    private void NotifySortChrome()
    {
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortByDate));
        OnPropertyChanged(nameof(IsSortByType));
        OnPropertyChanged(nameof(IsSortBySize));
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(IsFoldersFirst));
        OnPropertyChanged(nameof(IsFilesFirst));
        OnPropertyChanged(nameof(IsMixedFolderSort));
    }

    private void NotifyLayoutChrome()
    {
        OnPropertyChanged(nameof(IsDetailsViewActive));
        OnPropertyChanged(nameof(IsListViewActive));
        OnPropertyChanged(nameof(IsGridViewActive));
        OnPropertyChanged(nameof(IsMillerViewActive));
        OnPropertyChanged(nameof(ActiveThumbnailSize));
    }

    [RelayCommand]
    private void SetSortColumn(SortColumn column)
        => ActivePane?.SetSortColumnCommand.Execute(column);

    [RelayCommand]
    private void SetSortAscending()
        => ActivePane?.SetSortAscendingCommand.Execute(null);

    [RelayCommand]
    private void SetSortDescending()
        => ActivePane?.SetSortDescendingCommand.Execute(null);

    [RelayCommand]
    private void SetDirectorySort(DirectorySortMode mode)
        => ActivePane?.SetDirectorySortCommand.Execute(mode);

    private void RefreshHomeDashboard()
    {
        _homePage.SetRecentFiles(_recentPaths);
        _homePage.SetNetworkLocations(_lastNetworkLocations);
        _homePage.RefreshPins();
        _homePage.RefreshDrives();
    }

    private void OnHomeNavigateRequested(object? sender, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (string.Equals(path.TrimEnd('\\', '/'), _homePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            NavigateActivePane(pane => pane.NavigateToHome());
            return;
        }

        NavigateActivePane(pane => pane.NavigateTo(path));
    }

    private void NotifyGlobalFileCommandsCanExecuteChanged()
    {
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DeletePermanentlyCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        PinCurrentFolderCommand.NotifyCanExecuteChanged();
        OpenInTerminalCommand.NotifyCanExecuteChanged();
        RestoreFromRecycleBinCommand.NotifyCanExecuteChanged();
        EmptyRecycleBinCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleDualPane() => SelectedTab?.ToggleDualPaneCommand.Execute(null);

    [RelayCommand]
    private void ToggleFilter() => FocusFilter();

    [RelayCommand]
    private void FocusFilter() => SelectedTab?.ActivePane?.EnterFilterModeCommand.Execute(null);

    [RelayCommand]
    private void FocusSearch() => SelectedTab?.ActivePane?.EnterSearchModeCommand.Execute(null);

    private static bool CanUseGlobalFileShortcuts() => !TextInputFocus.IsActive();

    [RelayCommand]
    private void SetViewMode(LayoutMode mode) => SelectedTab?.ActivePane?.SetViewModeCommand.Execute(mode);

    private bool CanCutSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanCopySelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanDeleteSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanDeletePermanentlySelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForDeletePerm == true;

    private bool CanRenameSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSingleSelectionForOps == true;

    private bool CanPasteSelection() => CanUseGlobalFileShortcuts() && ActivePane?.CanPasteHere == true;

    private bool CanCreateFolder() => CanUseGlobalFileShortcuts() && ActivePane?.CanCreateFolderHere == true;

    private bool CanSelectAllEntries() => CanUseGlobalFileShortcuts() && ActivePane?.CanSelectAllHere == true;

    private bool CanClearSelection()
        => CanUseGlobalFileShortcuts() && ActivePane is { SelectedCount: > 0 };

    private bool CanInvertSelection() => CanSelectAllEntries();

    private bool CanCopyPathSelection() => CanUseGlobalFileShortcuts() && ActivePane?.CanCopyPath() == true;

    private bool CanPinCurrentFolder()
    {
        var path = ActivePane?.CurrentPath;
        if (string.IsNullOrEmpty(path) || ActivePane?.IsArchive == true)
            return false;

        return _sidebar.CanPinPath(path, GetSettings());
    }

    [RelayCommand(CanExecute = nameof(CanCutSelection))]
    private void Cut() => SelectedTab?.ActivePane?.CutCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private void Copy() => SelectedTab?.ActivePane?.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanPasteSelection))]
    private void Paste() => SelectedTab?.ActivePane?.PasteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void Delete() => SelectedTab?.ActivePane?.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDeletePermanentlySelection))]
    private void DeletePermanently() => SelectedTab?.ActivePane?.DeletePermanentlyCommand.Execute(null);

    private bool CanRestoreFromRecycleBin()
        => CanUseGlobalFileShortcuts() && ActivePane?.RestoreFromRecycleBinCommand.CanExecute(null) == true;

    private bool CanEmptyRecycleBin()
        => CanUseGlobalFileShortcuts() && ActivePane?.EmptyRecycleBinCommand.CanExecute(null) == true;

    [RelayCommand(CanExecute = nameof(CanRestoreFromRecycleBin))]
    private void RestoreFromRecycleBin() => ActivePane?.RestoreFromRecycleBinCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanEmptyRecycleBin))]
    private void EmptyRecycleBin() => ActivePane?.EmptyRecycleBinCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanRenameSelection))]
    private void Rename() => SelectedTab?.ActivePane?.BeginRenameCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanCreateFolder))]
    private void NewFolder() => SelectedTab?.ActivePane?.NewFolderCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanSelectAllEntries))]
    private void SelectAll() => SelectedTab?.ActivePane?.SelectAll();

    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection() => SelectedTab?.ActivePane?.ClearSelection();

    [RelayCommand(CanExecute = nameof(CanInvertSelection))]
    private void InvertSelection() => SelectedTab?.ActivePane?.InvertSelection();

    [RelayCommand(CanExecute = nameof(CanCopyPathSelection))]
    private void CopyPath() => ActivePane?.CopyPathCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanPinCurrentFolder))]
    private void PinCurrentFolder()
    {
        var path = ActivePane?.CurrentPath;
        if (!string.IsNullOrEmpty(path))
            PinPath(path);
    }

    [RelayCommand]
    private void ToggleShowHiddenFiles() => ShowHiddenFiles = !ShowHiddenFiles;

    [RelayCommand]
    private void ToggleShowFileExtensions() => ShowFileExtensions = !ShowFileExtensions;

    private void UpdateSidebarSelection(string path)
        => _sidebar.UpdateSelection(path, ActivePane?.IsHome == true);

    [RelayCommand]
    private void NavigateSidebar(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return;

        if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            NavigateActivePane(pane => pane.NavigateTo(item.Path));
            return;
        }

        if (item.Kind == SidebarItemKind.Home)
        {
            NavigateActivePane(pane => pane.NavigateToHome());
            return;
        }

        NavigateActivePane(pane => pane.NavigateTo(item.Path));
    }

    [RelayCommand]
    private void SetAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            AccentColorArgb = null;
            return;
        }

        AccentColorArgb = AccentColorDefaults.FromHex(hex);
    }

    public void PinPath(string path)
    {
        var settings = GetSettings();
        if (!_sidebar.TryPin(path, settings))
            return;

        SaveSettings(settings);
        RebuildSidebar();
        SelectedTab?.ActivePane?.NotifyPinStateChanged();
    }

    public void UnpinPath(string path)
    {
        var settings = GetSettings();
        if (!_sidebar.TryUnpin(path, settings))
            return;

        SaveSettings(settings);
        RebuildSidebar();
        SelectedTab?.ActivePane?.NotifyPinStateChanged();
    }

    public bool CanUnpinSidebarItem(SidebarItemViewModel? item)
        => _sidebar.CanUnpin(item, GetSettings());

    public bool CanPinSidebarItem(SidebarItemViewModel? item)
        => _sidebar.CanPin(item, GetSettings());

    private void OnPinPathRequested(object? sender, (string Path, bool Pin) args)
    {
        if (args.Pin)
            PinPath(args.Path);
        else
            UnpinPath(args.Path);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        Theme = _themeService.CurrentMode switch
        {
            ThemeMode.Light => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.System,
            _ => ThemeMode.Light
        };
    }

    public async Task HandleSidebarDropAsync(IReadOnlyList<string> paths, string destinationPath, bool isCopy)
    {
        var pane = GetOrCreateBrowserTab().ActivePane;
        if (pane is null || !pane.CanAcceptFileDrop)
            return;

        await pane.HandleDropAsync(paths, destinationPath, isCopy);
    }

    public void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _networkCts?.Cancel();
        _networkCts?.Dispose();

        _networkAvailability.AvailabilityChanged -= OnNetworkAvailabilityChanged;
        _folderColors.ColorsChanged -= OnFolderColorsChanged;
        _themeService.ThemeChanged -= OnThemeServiceChanged;
        _operationReporter.PropertyChanged -= OnOperationReporterPropertyChanged;
        _operationReporter.Dispose();

        // WindowHostService owns the session-save policy so secondary scoped windows do not
        // overwrite the persisted session when their scopes are disposed.
        PersistChromeSettings();
        FlushSettings();

        foreach (var tab in Tabs)
        {
            DetachTab(tab);
            tab.Dispose();
        }

        Tabs.Clear();
    }
}
