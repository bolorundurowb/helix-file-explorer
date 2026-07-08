using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly IThemeService _themeService;
    private readonly IFileSystemProvider _fileSystem;
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IVolumeProvider _volumes;
    private readonly IFileOperationService _fileOps;
    private readonly IClipboardService _clipboard;
    private readonly IOsFileClipboard _osClipboard;
    private readonly Func<IFileChangeWatcher> _watcherFactory;
    private readonly string _homePath;
    private bool _disposed;

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        ISessionStore sessionStore,
        IThemeService themeService,
        IFileSystemProvider fileSystem,
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        IFileOperationService fileOps,
        IClipboardService clipboard,
        IOsFileClipboard osClipboard,
        Func<IFileChangeWatcher> watcherFactory)
    {
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _themeService = themeService;
        _fileSystem = fileSystem;
        _quickAccess = quickAccess;
        _volumes = volumes;
        _fileOps = fileOps;
        _clipboard = clipboard;
        _osClipboard = osClipboard;
        _watcherFactory = watcherFactory;

        _homePath = _quickAccess.GetPath(KnownFolderKind.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var settings = _settingsStore.Load();
        IsSidebarOpen = settings.SidebarOpen;

        SidebarItems = SidebarFactory.Build(_quickAccess, _volumes);

        RestoreSession();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePane))]
    [NotifyPropertyChangedFor(nameof(HasMultipleTabs))]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private string _title = "Helix Explorer";

    [ObservableProperty]
    private bool _isSidebarOpen = true;

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
        var tab = new TabViewModel(_fileSystem, _fileOps, _clipboard, _osClipboard, _watcherFactory);
        tab.CloseRequested += OnTabCloseRequested;
        tab.Navigated += OnTabNavigated;
        return tab;
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

        SyncChromeToActivePane();
    }

    partial void OnSelectedTabChanged(TabViewModel? value) => SyncChromeToActivePane();

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
            return;

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

        SaveSession();

        foreach (var tab in Tabs)
        {
            tab.CloseRequested -= OnTabCloseRequested;
            tab.Navigated -= OnTabNavigated;
            tab.Dispose();
        }

        Tabs.Clear();
    }
}
