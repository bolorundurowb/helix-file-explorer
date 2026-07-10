using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Search;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.Input;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly IThemeService _themeService;
    private readonly IAccentBrushService _accentBrushes;
    private readonly IFileSystemProvider _fileSystem;
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IVolumeProvider _volumes;
    private readonly INetworkLocationProvider _networkLocations;
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IOsFileClipboard _osClipboard;
    private readonly IShellContextMenuService _shellContextMenu;
    private readonly IUiHost _uiHost;
    private readonly IGitProvider _git;
    private readonly IArchiveProvider _archive;
    private readonly IFolderColorService _folderColors;
    private readonly FileVisualService _visuals;
    private readonly Func<IFileChangeWatcher> _watcherFactory;
    private readonly FileOperationReporter _operationReporter;
    private readonly IUserDialogService _dialogs;
    private readonly IWindowHostService _windowHost;
    private readonly IShellFolderEnumerator _shellEnumerator;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly HomePageViewModel _homePage;
    private readonly SettingsPageViewModel _settingsPage;
    private readonly string _homePath;
    private readonly List<CommandItem> _allCommands = new();
    private readonly List<string> _recentPaths = new();
    private IReadOnlyList<NetworkLocationInfo> _lastNetworkLocations = [];
    private CancellationTokenSource? _networkCts;
    private bool _commandsBuilt;
    private bool _disposed;
    private TabViewModel? _lastBrowserTab;

    private const int MaxPaletteResults = 24;

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        ISessionStore sessionStore,
        IThemeService themeService,
        IAccentBrushService accentBrushes,
        IFileSystemProvider fileSystem,
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        INetworkLocationProvider networkLocations,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        IShellContextMenuService shellContextMenu,
        IUiHost uiHost,
        IGitProvider git,
        IArchiveProvider archive,
        IFolderColorService folderColors,
        FileVisualService visuals,
        Func<IFileChangeWatcher> watcherFactory,
        FileOperationReporter operationReporter,
        IUserDialogService dialogs,
        IWindowHostService windowHost,
        IShellFolderEnumerator shellEnumerator,
        ITerminalLauncher terminalLauncher,
        HomePageViewModel homePage)
    {
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _themeService = themeService;
        _accentBrushes = accentBrushes;
        _fileSystem = fileSystem;
        _quickAccess = quickAccess;
        _volumes = volumes;
        _networkLocations = networkLocations;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _shellContextMenu = shellContextMenu;
        _uiHost = uiHost;
        _git = git;
        _archive = archive;
        _folderColors = folderColors;
        _visuals = visuals;
        _watcherFactory = watcherFactory;
        _operationReporter = operationReporter;
        _dialogs = dialogs;
        _windowHost = windowHost;
        _shellEnumerator = shellEnumerator;
        _terminalLauncher = terminalLauncher;
        OperationReporter = operationReporter;
        _homePage = homePage;
        _settingsPage = new SettingsPageViewModel(this);
        _homePage.NavigateRequested += OnHomeNavigateRequested;

        _homePath = _quickAccess.GetPath(KnownFolderKind.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = _settingsStore.Load();
        SidebarWidth = Math.Clamp(settings.SidebarWidth, 160, 480);
        ShowHiddenFiles = settings.ShowHiddenFiles;
        ShowFileExtensions = settings.ShowFileExtensions;
        Theme = settings.Theme;
        SizeDisplay = settings.SizeDisplay;
        DefaultViewMode = settings.DefaultViewMode;
        DefaultThumbnailSize = Math.Clamp(settings.DefaultThumbnailSize, PaneViewModel.MinThumbnailSize, PaneViewModel.MaxThumbnailSize);
        DefaultDualPane = settings.DefaultDualPane;
        DefaultSplitOrientation = settings.DefaultSplitOrientation;
        AccentColorArgb = settings.AccentColorArgb;

        _accentBrushes.ApplyCustomAccent(AccentColorArgb);
        _themeService.ThemeChanged += OnThemeServiceChanged;

        SidebarItems = SidebarFactory.Build(
            _quickAccess,
            _volumes,
            settings.PinnedPaths,
            settings.UnpinnedPaths);
        _ = LoadSidebarIconsAsync();
        _ = RefreshNetworkLocationsAsync();

        _folderColors.ColorsChanged += OnFolderColorsChanged;
    }

    public void InitializeWindow(bool restoreSession, string? initialPath = null)
    {
        if (restoreSession)
        {
            RestoreSession();
            return;
        }

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var tab = CreateDefaultTab();
            tab.LeftPane.NavigateTo(initialPath);
            AddTab(tab);
            SelectedTab = tab;
            return;
        }

        AddTab(CreateDefaultTab());
        SelectedTab = Tabs[0];
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; }

    public FileOperationReporter OperationReporter { get; }

    public bool IsDualPaneActive => SelectedTab?.IsDualPane == true;

    public ObservableCollection<CommandItem> FilteredCommands { get; } = new();

    [ObservableProperty] private bool _isCommandPaletteOpen;

    [ObservableProperty] private string _commandPaletteQuery = string.Empty;

    [ObservableProperty]
    private bool _isDiscoveringNetwork;

    [ObservableProperty]
    private string _networkBannerText = "Discovering network shares…";

    private async Task RefreshNetworkLocationsAsync()
    {
        _networkCts?.Cancel();
        _networkCts = new CancellationTokenSource();
        var ct = _networkCts.Token;

        IsDiscoveringNetwork = true;
        try
        {
            var locations = await _networkLocations.GetNetworkLocationsAsync(ct).ConfigureAwait(true);
            if (!ct.IsCancellationRequested)
            {
                _lastNetworkLocations = locations;
                RebuildSidebar(locations);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Network discovery is opportunistic; keep the stable fallback item.
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

    private void RebuildSidebar(IReadOnlyList<NetworkLocationInfo> networkLocations)
    {
        var selectedPath = ActivePane?.CurrentPath;
        var settings = _settingsStore.Load();
        var items = SidebarFactory.Build(
            _quickAccess,
            _volumes,
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            networkLocations,
            selectedPath);
        SidebarItems.Clear();
        foreach (var item in items)
            SidebarItems.Add(item);

        _ = LoadSidebarIconsAsync();
    }

    private async Task LoadSidebarIconsAsync()
    {
        foreach (var item in SidebarItems)
        {
            if (!item.IsNavigable || string.IsNullOrEmpty(item.Path))
                continue;

            try
            {
                var icon = await _visuals.GetBitmapAsync(
                    item.Path,
                    isDirectory: true,
                    size: 16,
                    preferThumbnail: false,
                    CancellationToken.None).ConfigureAwait(true);
                item.Icon = icon;
            }
            catch
            {
                item.Icon = null;
            }
        }
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
    private double _sidebarWidth = 200;

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
    private ThemeMode _theme = ThemeMode.System;

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

    partial void OnThemeChanged(ThemeMode value)
    {
        _themeService.ApplyTheme(value);
        PersistChromeSettings();
        _accentBrushes.ApplyCustomAccent(AccentColorArgb);
    }

    partial void OnSizeDisplayChanged(SizeDisplayMode value)
    {
        PersistChromeSettings();
        SizeDisplayChanged?.Invoke(value);
        RefreshSizeDisplayOnAllPanes();
    }

    partial void OnSidebarWidthChanged(double value)
    {
        var clamped = Math.Clamp(value, 160, 480);
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

    partial void OnAccentColorArgbChanged(uint? value)
    {
        _accentBrushes.ApplyCustomAccent(value);
        PersistChromeSettings();
    }

    private void OnThemeServiceChanged(ThemeMode _)
        => _accentBrushes.ApplyCustomAccent(AccentColorArgb);

    private void PersistViewSettings()
    {
        var settings = _settingsStore.Load();
        settings.ShowHiddenFiles = ShowHiddenFiles;
        settings.ShowFileExtensions = ShowFileExtensions;
        _settingsStore.Save(settings);
    }

    private void PersistChromeSettings()
    {
        var settings = _settingsStore.Load();
        settings.SidebarWidth = SidebarWidth;
        settings.Theme = Theme;
        settings.SizeDisplay = SizeDisplay;
        settings.DefaultViewMode = DefaultViewMode;
        settings.DefaultThumbnailSize = DefaultThumbnailSize;
        settings.DefaultDualPane = DefaultDualPane;
        settings.DefaultSplitOrientation = DefaultSplitOrientation;
        settings.AccentColorArgb = AccentColorArgb;
        _settingsStore.Save(settings);
    }

    private void ApplyViewSettingsToTabs()
    {
        foreach (var tab in Tabs)
            tab.ApplyViewSettings(ShowHiddenFiles, ShowFileExtensions);
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
        var session = _sessionStore.Load();

        if (session.RecentPaths.Count > 0)
        {
            _recentPaths.Clear();
            foreach (var path in session.RecentPaths)
                _recentPaths.Add(path);
        }

        if (session.Tabs.Count == 0)
        {
            AddTab(CreateDefaultTab());
        }
        else
        {
            foreach (var snapshot in session.Tabs)
            {
                var tab = CreateTab();
                AddTab(tab);
                tab.RestoreFrom(snapshot);
            }
        }

        var index = Math.Clamp(session.ActiveTabIndex, 0, Tabs.Count - 1);
        SelectedTab = Tabs[index];
    }

    public void SaveSession()
    {
        var document = new SessionDocument
        {
            ActiveTabIndex = SelectedTab is null ? 0 : Math.Max(0, Tabs.IndexOf(SelectedTab)),
            RecentPaths = _recentPaths.Take(12).ToList()
        };

        foreach (var tab in Tabs.Where(t => t.IsBrowserTab))
            document.Tabs.Add(tab.CreateSnapshot());

        try
        {
            _sessionStore.Save(document);
        }
        catch
        {
        }
    }

    private TabViewModel CreateTab(TabKind kind = TabKind.Browser)
    {
        var tab = new TabViewModel(
            _fileSystem,
            _fileOps,
            _clipboard,
            _osClipboard,
            _shellContextMenu,
            _uiHost,
            _git,
            _archive,
            _folderColors,
            _settingsStore,
            _visuals,
            _watcherFactory,
            _operationReporter,
            _quickAccess,
            _dialogs,
            _windowHost,
            _shellEnumerator,
            _terminalLauncher,
            _homePage,
            kind,
            kind == TabKind.Settings ? _settingsPage : null);
        tab.CloseRequested += OnTabCloseRequested;
        tab.SortChanged += OnTabSortChanged;
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
        tab.ApplyViewSettings(ShowHiddenFiles, ShowFileExtensions);
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
        if (tab is null)
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        var wasSelected = ReferenceEquals(tab, SelectedTab);

        Tabs.Remove(tab);
        tab.CloseRequested -= OnTabCloseRequested;
        tab.SortChanged -= OnTabSortChanged;
        tab.Navigated -= OnTabNavigated;
        tab.OpenInNewTabRequested -= OnOpenInNewTabRequested;
        tab.PinPathRequested -= OnPinPathRequested;
        tab.SelectionChanged -= OnTabSelectionChanged;
        tab.Dispose();

        if (Tabs.Count == 0)
        {
            var replacement = CreateDefaultTab();
            AddTab(replacement);
            SelectedTab = replacement;
        }
        else if (wasSelected)
        {
            SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    [RelayCommand]
    private void CloseSelectedTab() => CloseTab(SelectedTab);

    [RelayCommand]
    private void SelectNextTab() => CycleSelectedTab(1);

    [RelayCommand]
    private void SelectPreviousTab() => CycleSelectedTab(-1);

    public void CycleSelectedTab(int delta)
    {
        if (Tabs.Count < 2 || SelectedTab is null)
            return;

        var current = Tabs.IndexOf(SelectedTab);
        var next = (current + delta % Tabs.Count + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[next];
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab)
            CloseTab(tab);
    }

    private void OnTabSortChanged(object? sender, EventArgs e) => NotifySortChrome();

    private void OnTabNavigated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTab))
            return;

        RecordRecent(SelectedTab?.ActivePane.CurrentPath);
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

    private void EnsureCommandsBuilt()
    {
        if (_commandsBuilt)
            return;

        _commandsBuilt = true;
        _allCommands.Add(new CommandItem("New Tab", "File", vm => vm.NewTabCommand.Execute(null), "Ctrl+T"));
        _allCommands.Add(new CommandItem("Close Tab", "File", vm => vm.CloseSelectedTabCommand.Execute(null), "Ctrl+W"));
        _allCommands.Add(new CommandItem("Toggle Theme", "Appearance", vm => vm.ToggleThemeCommand.Execute(null), "Ctrl+Shift+T"));
        _allCommands.Add(new CommandItem("Toggle Dual Pane", "View", vm => vm.ToggleDualPaneCommand.Execute(null), "Ctrl+D"));
        _allCommands.Add(new CommandItem("Search", "View", vm => vm.FocusSearchCommand.Execute(null), "Ctrl+F"));
        _allCommands.Add(new CommandItem("Settings", "View", vm => vm.OpenSettingsCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Back", "Navigation", vm => vm.ActivePane?.GoBackCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Forward", "Navigation", vm => vm.ActivePane?.GoForwardCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Up", "Navigation", vm => vm.ActivePane?.GoUpCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Refresh", "Navigation", vm => vm.ActivePane?.RefreshCommand.Execute(null), "F5"));
        _allCommands.Add(new CommandItem("New Folder", "File", vm => vm.NewFolderCommand.Execute(null), "Ctrl+Shift+N"));
        _allCommands.Add(new CommandItem("Details View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Details)));
        _allCommands.Add(new CommandItem("List View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.List)));
        _allCommands.Add(new CommandItem("Grid View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Grid)));
        _allCommands.Add(new CommandItem("Miller Columns", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Miller)));
        _allCommands.Add(new CommandItem("Show Hidden Items", "View", vm => vm.ToggleShowHiddenFilesCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Show File Extensions", "View", vm => vm.ToggleShowFileExtensionsCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Copy Path", "File", vm => vm.CopyPathCommand.Execute(null), "Ctrl+Shift+C"));
        _allCommands.Add(new CommandItem("Pin Current Folder", "View", vm => vm.PinCurrentFolderCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Cut", "File", vm => vm.CutCommand.Execute(null), "Ctrl+X"));
        _allCommands.Add(new CommandItem("Copy", "File", vm => vm.CopyCommand.Execute(null), "Ctrl+C"));
        _allCommands.Add(new CommandItem("Paste", "File", vm => vm.PasteCommand.Execute(null), "Ctrl+V"));
        _allCommands.Add(new CommandItem("Delete", "File", vm => vm.DeleteCommand.Execute(null), "Delete"));
        _allCommands.Add(new CommandItem("Delete Permanently", "File", vm => vm.DeletePermanentlyCommand.Execute(null), "Shift+Delete"));
        _allCommands.Add(new CommandItem("Next Tab", "View", vm => vm.SelectNextTabCommand.Execute(null), "Ctrl+Tab"));
        _allCommands.Add(new CommandItem("Previous Tab", "View", vm => vm.SelectPreviousTabCommand.Execute(null), "Ctrl+Shift+Tab"));
        _allCommands.Add(new CommandItem("Rename", "File", vm => vm.RenameCommand.Execute(null), "F2"));
        _allCommands.Add(new CommandItem("Git: Switch Branch", "Git", vm => _ = vm.ActivePane?.OpenBranchFlyoutCommand.ExecuteAsync(null)));
    }

    private void RefreshCommandPalette(string query)
    {
        EnsureCommandsBuilt();
        FilteredCommands.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var command in _allCommands)
                FilteredCommands.Add(command);
            return;
        }

        var scored = new List<(int score, CommandItem item)>();
        foreach (var command in _allCommands)
        {
            var score = command.FuzzyScore(query);
            if (score >= 0)
                scored.Add((score, command));
        }

        foreach (var path in _recentPaths)
        {
            var score = FuzzyMatcher.Score(path, query);
            if (score >= 0)
            {
                var captured = path;
                scored.Add((score, new CommandItem(path, "Recent", vm => vm.NavigateActive(captured))));
            }
        }

        foreach (var entry in scored.OrderByDescending(t => t.score).Take(MaxPaletteResults))
            FilteredCommands.Add(entry.item);
    }

    private void RecordRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.Equals(path, PaneViewModel.HomeRoute, StringComparison.Ordinal))
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

    private void NavigateActive(string path) => NavigateActivePane(pane => pane.NavigateTo(path));

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (!IsCommandPaletteOpen)
            return;

        IsStatusCentreOpen = false;
        foreach (var tab in Tabs.Where(t => t.IsBrowserTab))
            RecordRecent(tab.ActivePane.CurrentPath);

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
        foreach (var item in SidebarItems)
        {
            if (item.IsNavigable)
                item.NotifyFolderColorChanged();
        }

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
        var path = SelectedTab?.ActivePane.CurrentPath ?? string.Empty;
        UpdateSidebarSelection(path);
        RefreshHomeDashboard();
        Title = SelectedTab is null || string.IsNullOrEmpty(SelectedTab.Title)
            ? "Helix Explorer"
            : $"{SelectedTab.Title} — Helix Explorer";
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(IsDualPaneActive));
        OnPropertyChanged(nameof(IsBrowserTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(ShowFileToolbar));
        OnPropertyChanged(nameof(ShowBrowserChrome));
        NotifyGlobalFileCommandsCanExecuteChanged();
        NotifySortChrome();
    }

    public bool IsSortByName => ActivePane?.IsSortByName == true;
    public bool IsSortByDate => ActivePane?.IsSortByDate == true;
    public bool IsSortByType => ActivePane?.IsSortByType == true;
    public bool IsSortBySize => ActivePane?.IsSortBySize == true;
    public bool IsSortAscending => ActivePane?.IsSortAscending == true;
    public bool IsSortDescending => ActivePane?.IsSortDescendingActive == true;

    private void NotifySortChrome()
    {
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortByDate));
        OnPropertyChanged(nameof(IsSortByType));
        OnPropertyChanged(nameof(IsSortBySize));
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescending));
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
        CopyPathCommand.NotifyCanExecuteChanged();
        PinCurrentFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleDualPane() => SelectedTab?.ToggleDualPaneCommand.Execute(null);

    [RelayCommand]
    private void ToggleFilter() => FocusSearch();

    [RelayCommand]
    private void FocusSearch() => SelectedTab?.ActivePane.EnterSearchModeCommand.Execute(null);

    private static bool CanUseGlobalFileShortcuts() => !TextInputFocus.IsActive();

    [RelayCommand]
    private void SetViewMode(LayoutMode mode) => SelectedTab?.ActivePane.SetViewModeCommand.Execute(mode);

    private bool CanCutSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanCopySelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanDeleteSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanRenameSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSingleSelectionForOps == true;

    private bool CanPasteSelection() => CanUseGlobalFileShortcuts() && ActivePane?.CanPasteHere == true;

    private bool CanCreateFolder() => CanUseGlobalFileShortcuts() && ActivePane?.CanCreateFolderHere == true;

    private bool CanSelectAllEntries() => CanUseGlobalFileShortcuts() && ActivePane?.CanSelectAllHere == true;

    private bool CanCopyPathSelection() => CanUseGlobalFileShortcuts() && ActivePane?.HasSelectionForOps == true;

    private bool CanPinCurrentFolder()
    {
        var path = ActivePane?.CurrentPath;
        if (string.IsNullOrEmpty(path) || ActivePane?.IsArchive == true || !Directory.Exists(path))
            return false;

        var settings = _settingsStore.Load();
        return !PinnedPathHelper.IsPinnedOrDefault(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            path);
    }

    [RelayCommand(CanExecute = nameof(CanCutSelection))]
    private void Cut() => SelectedTab?.ActivePane.CutCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private void Copy() => SelectedTab?.ActivePane.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanPasteSelection))]
    private void Paste() => SelectedTab?.ActivePane.PasteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void Delete() => SelectedTab?.ActivePane.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void DeletePermanently() => SelectedTab?.ActivePane.DeletePermanentlyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanRenameSelection))]
    private void Rename() => SelectedTab?.ActivePane.BeginRenameCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanCreateFolder))]
    private void NewFolder() => SelectedTab?.ActivePane.NewFolderCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanSelectAllEntries))]
    private void SelectAll() => SelectedTab?.ActivePane.SelectAll();

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
    {
        foreach (var item in SidebarItems)
        {
            if (item.IsSectionHeader)
                continue;

            if (item.Kind == SidebarItemKind.Home)
            {
                item.IsSelected = ActivePane?.IsHome == true;
                continue;
            }

            item.IsSelected = !string.IsNullOrEmpty(item.Path)
                && string.Equals(
                    item.Path.TrimEnd('\\', '/'),
                    path.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

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
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        var settings = _settingsStore.Load();
        var normalized = NormalizePinnedPath(path);
        settings.UnpinnedPaths.RemoveAll(p =>
            string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase));

        if (!PinnedPathHelper.IsPinned(settings.PinnedPaths, normalized))
            settings.PinnedPaths.Insert(0, normalized);

        _settingsStore.Save(settings);
        RebuildSidebar(_lastNetworkLocations);
        SelectedTab?.ActivePane.NotifyPinStateChanged();
    }

    public void UnpinPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var settings = _settingsStore.Load();
        var normalized = NormalizePinnedPath(path);
        settings.PinnedPaths.RemoveAll(p =>
            string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase));

        var defaults = GetDefaultPinnedPaths();
        if (defaults.Any(d => string.Equals(NormalizePinnedPath(d), normalized, StringComparison.OrdinalIgnoreCase))
            && !settings.UnpinnedPaths.Any(p =>
                string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            settings.UnpinnedPaths.Add(normalized);
        }

        _settingsStore.Save(settings);
        RebuildSidebar(_lastNetworkLocations);
        SelectedTab?.ActivePane.NotifyPinStateChanged();
    }

    public bool CanUnpinSidebarItem(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return false;

        var settings = _settingsStore.Load();
        return PinnedPathHelper.IsVisibleInSidebar(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            item.Path);
    }

    public bool CanPinSidebarItem(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return false;

        if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return false;

        var settings = _settingsStore.Load();
        return !PinnedPathHelper.IsPinnedOrDefault(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            item.Path);
    }

    private IReadOnlyList<string> GetDefaultPinnedPaths()
        => _quickAccess.GetPinnedDefaults()
            .Select(t => t.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

    private static string NormalizePinnedPath(string path)
        => path.TrimEnd('\\', '/');

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _networkCts?.Cancel();
        _networkCts?.Dispose();

        _folderColors.ColorsChanged -= OnFolderColorsChanged;
        _themeService.ThemeChanged -= OnThemeServiceChanged;
        _operationReporter.Dispose();

        SaveSession();

        foreach (var tab in Tabs)
        {
            tab.CloseRequested -= OnTabCloseRequested;
            tab.SortChanged -= OnTabSortChanged;
            tab.Navigated -= OnTabNavigated;
            tab.OpenInNewTabRequested -= OnOpenInNewTabRequested;
            tab.PinPathRequested -= OnPinPathRequested;
            tab.SelectionChanged -= OnTabSelectionChanged;
            tab.Dispose();
        }

        Tabs.Clear();
    }
}
