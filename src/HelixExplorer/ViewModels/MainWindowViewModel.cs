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
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly IThemeService _themeService;
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
    private readonly string _homePath;
    private readonly List<CommandItem> _allCommands = new();
    private readonly List<string> _recentPaths = new();
    private CancellationTokenSource? _networkCts;
    private bool _commandsBuilt;
    private bool _disposed;

    private const int MaxPaletteResults = 24;

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        ISessionStore sessionStore,
        IThemeService themeService,
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
        Func<IFileChangeWatcher> watcherFactory)
    {
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _themeService = themeService;
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

        _homePath = _quickAccess.GetPath(KnownFolderKind.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = _settingsStore.Load();
        IsSidebarOpen = settings.SidebarOpen;

        SidebarItems = SidebarFactory.Build(_quickAccess, _volumes);

        RestoreSession();
        _ = RefreshNetworkLocationsAsync();

        _folderColors.ColorsChanged += OnFolderColorsChanged;
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; }

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
                RebuildSidebar(locations);
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
                IsDiscoveringNetwork = false;
        }
    }

    private void RebuildSidebar(IReadOnlyList<NetworkLocationInfo> networkLocations)
    {
        var selectedPath = ActivePane?.CurrentPath;
        var items = SidebarFactory.Build(_quickAccess, _volumes, networkLocations, selectedPath);
        SidebarItems.Clear();
        foreach (var item in items)
            SidebarItems.Add(item);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePane))]
    [NotifyPropertyChangedFor(nameof(HasMultipleTabs))]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private string _title = "Helix Explorer";

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private bool _isWindowActive = true;

    public PaneViewModel? ActivePane => SelectedTab?.ActivePane;

    public bool HasMultipleTabs => Tabs.Count > 1;

    private void RestoreSession()
    {
        var session = _sessionStore.Load();

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
            SidebarOpen = IsSidebarOpen,
            ActiveTabIndex = SelectedTab is null ? 0 : Math.Max(0, Tabs.IndexOf(SelectedTab))
        };

        foreach (var tab in Tabs)
            document.Tabs.Add(tab.CreateSnapshot());

        try
        {
            _sessionStore.Save(document);
        }
        catch
        {
        }
    }

    private TabViewModel CreateTab()
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
            _visuals,
            _watcherFactory);
        tab.CloseRequested += OnTabCloseRequested;
        tab.Navigated += OnTabNavigated;
        tab.OpenInNewTabRequested += OnOpenInNewTabRequested;
        tab.SetSelectionActive(IsWindowActive);
        return tab;
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
        tab.LeftPane.NavigateTo(_homePath);
        return tab;
    }

    private void AddTab(TabViewModel tab)
    {
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
        tab.Navigated -= OnTabNavigated;
        tab.OpenInNewTabRequested -= OnOpenInNewTabRequested;
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

    private void OnTabNavigated(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTab))
            return;

        RecordRecent(SelectedTab?.ActivePane.CurrentPath);
        SyncChromeToActivePane();
    }

    partial void OnSelectedTabChanged(TabViewModel? value) => SyncChromeToActivePane();

    partial void OnCommandPaletteQueryChanged(string value) => RefreshCommandPalette(value);

    private void EnsureCommandsBuilt()
    {
        if (_commandsBuilt)
            return;

        _commandsBuilt = true;
        _allCommands.Add(new CommandItem("New Tab", "File", vm => vm.NewTabCommand.Execute(null), "Ctrl+T"));
        _allCommands.Add(new CommandItem("Close Tab", "File", vm => vm.CloseSelectedTabCommand.Execute(null), "Ctrl+W"));
        _allCommands.Add(new CommandItem("Toggle Theme", "Appearance", vm => vm.ToggleThemeCommand.Execute(null), "Ctrl+Shift+T"));
        _allCommands.Add(new CommandItem("Toggle Sidebar", "View", vm => vm.ToggleSidebarCommand.Execute(null), "Ctrl+B"));
        _allCommands.Add(new CommandItem("Toggle Dual Pane", "View", vm => vm.ToggleDualPaneCommand.Execute(null), "Ctrl+D"));
        _allCommands.Add(new CommandItem("Toggle Filter", "View", vm => vm.ToggleFilterCommand.Execute(null), "Ctrl+F"));
        _allCommands.Add(new CommandItem("Go Back", "Navigation", vm => vm.ActivePane?.GoBackCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Forward", "Navigation", vm => vm.ActivePane?.GoForwardCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Up", "Navigation", vm => vm.ActivePane?.GoUpCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Refresh", "Navigation", vm => vm.ActivePane?.RefreshCommand.Execute(null), "F5"));
        _allCommands.Add(new CommandItem("New Folder", "File", vm => vm.NewFolderCommand.Execute(null), "Ctrl+Shift+N"));
        _allCommands.Add(new CommandItem("Details View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Details)));
        _allCommands.Add(new CommandItem("List View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.List)));
        _allCommands.Add(new CommandItem("Grid View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Grid)));
        _allCommands.Add(new CommandItem("Miller Columns", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Miller)));
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
        if (string.IsNullOrWhiteSpace(path))
            return;

        _recentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentPaths.Insert(0, path);
        if (_recentPaths.Count > 12)
            _recentPaths.RemoveAt(_recentPaths.Count - 1);
    }

    private void NavigateActive(string path) => SelectedTab?.ActivePane.NavigateTo(path);

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (!IsCommandPaletteOpen)
            return;

        foreach (var tab in Tabs)
            RecordRecent(tab.ActivePane.CurrentPath);

        CommandPaletteQuery = string.Empty;
        RefreshCommandPalette(string.Empty);
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
        Title = SelectedTab is null || string.IsNullOrEmpty(SelectedTab.Title)
            ? "Helix Explorer"
            : $"{SelectedTab.Title} — Helix Explorer";
        OnPropertyChanged(nameof(ActivePane));
    }

    [RelayCommand]
    private void ToggleDualPane() => SelectedTab?.ToggleDualPaneCommand.Execute(null);

    [RelayCommand]
    private void ToggleFilter() => SelectedTab?.ActivePane.ToggleFilterCommand.Execute(null);

    [RelayCommand]
    private void SetViewMode(LayoutMode mode) => SelectedTab?.ActivePane.SetViewModeCommand.Execute(mode);

    [RelayCommand]
    private void Cut() => SelectedTab?.ActivePane.CutCommand.Execute(null);

    [RelayCommand]
    private void Copy() => SelectedTab?.ActivePane.CopyCommand.Execute(null);

    [RelayCommand]
    private void Paste() => SelectedTab?.ActivePane.PasteCommand.Execute(null);

    [RelayCommand]
    private void Delete() => SelectedTab?.ActivePane.DeleteCommand.Execute(null);

    [RelayCommand]
    private void Rename() => SelectedTab?.ActivePane.BeginRenameCommand.Execute(null);

    [RelayCommand]
    private void NewFolder() => SelectedTab?.ActivePane.NewFolderCommand.Execute(null);

    [RelayCommand]
    private void SelectAll() => SelectedTab?.ActivePane.SelectAll();

    private void UpdateSidebarSelection(string path)
    {
        foreach (var item in SidebarItems)
        {
            if (item.IsSectionHeader)
                continue;

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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = item.Path,
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            return;
        }

        SelectedTab?.ActivePane.NavigateTo(item.Path);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
        var settings = _settingsStore.Load();
        settings.SidebarOpen = IsSidebarOpen;
        _settingsStore.Save(settings);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _themeService.CurrentMode switch
        {
            ThemeMode.Light => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.System,
            _ => ThemeMode.Light
        };
        _themeService.ApplyTheme(next);
        var settings = _settingsStore.Load();
        settings.Theme = next;
        _settingsStore.Save(settings);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _networkCts?.Cancel();
        _networkCts?.Dispose();

        _folderColors.ColorsChanged -= OnFolderColorsChanged;

        SaveSession();

        foreach (var tab in Tabs)
        {
            tab.CloseRequested -= OnTabCloseRequested;
            tab.Navigated -= OnTabNavigated;
            tab.OpenInNewTabRequested -= OnOpenInNewTabRequested;
            tab.Dispose();
        }

        Tabs.Clear();
    }
}
