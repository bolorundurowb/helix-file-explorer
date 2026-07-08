using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Models;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>Root view-model — owns tabs, the command palette, and session persistence.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxRecentPaths = 40;
    private const int MaxPaletteResults = 25;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private TabViewModel? _activeTab;

    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private string _commandPaletteQuery = string.Empty;
    [ObservableProperty] private bool _isSidebarOpen = true;

    public ObservableCollection<CommandItem> FilteredCommands { get; } = new();
    public ObservableCollection<SidebarNode> Sidebar { get; } = new();

    private readonly List<CommandItem> _allCommands = new();
    private readonly List<string> _recentPaths = new();
    private bool _commandsBuilt;

    public MainWindowViewModel() : this(restoreSession: true) { }

    public MainWindowViewModel(bool restoreSession)
    {
        ServiceLocator.Theme.ThemeChanged += OnThemeChanged;

        if (restoreSession) RestoreSession();
        if (Tabs.Count == 0) OpenNewTabCommand.Execute(null);

        foreach (var root in SidebarNode.BuildRoots())
        {
            Sidebar.Add(root);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ThemeAccent));
        OnPropertyChanged(nameof(ThemeAccentBrush));
    }

    /// <summary>The active accent colour for headers/buttons.</summary>
    public Avalonia.Media.Color ThemeAccent => ServiceLocator.Theme.Accent;
    public Avalonia.Media.SolidColorBrush ThemeAccentBrush => new(ServiceLocator.Theme.Accent);

    // ---- command palette ----

    private void EnsureCommandsBuilt()
    {
        if (_commandsBuilt) return;
        _commandsBuilt = true;

        _allCommands.Add(new CommandItem("New Tab", "File", vm => vm.OpenNewTab(), "Ctrl+T"));
        _allCommands.Add(new CommandItem("Close Tab", "File", vm => vm.CloseTab(), "Ctrl+W"));
        _allCommands.Add(new CommandItem("Toggle Theme", "Appearance", _ => ServiceLocator.Theme.ToggleTheme(), "Ctrl+Shift+T"));
        _allCommands.Add(new CommandItem("Follow System Theme", "Appearance", _ => ServiceLocator.Theme.FollowSystemTheme = true));
        _allCommands.Add(new CommandItem("Open Settings", "Application", vm => vm.OpenSettings()));
        _allCommands.Add(new CommandItem("Toggle Sidebar", "View", vm => vm.IsSidebarOpen = !vm.IsSidebarOpen, "Ctrl+B"));
        _allCommands.Add(new CommandItem("Toggle Dual Pane", "View", vm =>
        {
            if (vm.ActiveTab != null) vm.ActiveTab.IsDualPane = !vm.ActiveTab.IsDualPane;
        }, "Ctrl+D"));
        _allCommands.Add(new CommandItem("Toggle Split Orientation", "View", vm => vm.ActiveTab?.ToggleOrientation()));
        _allCommands.Add(new CommandItem("Swap Panes", "View", vm => vm.ActiveTab?.SwapPanes()));
        _allCommands.Add(new CommandItem("Go Up", "Navigation", vm => vm.ActiveTab?.ActivePane.NavigateTo("..")));
        _allCommands.Add(new CommandItem("Git: Switch Branch", "Git", vm => vm.ActiveTab?.ActivePane.OpenBranchFlyoutCommand.Execute(null)));
    }

    partial void OnCommandPaletteQueryChanged(string value) => RefreshPalette(value);

    private void RefreshPalette(string query)
    {
        EnsureCommandsBuilt();
        FilteredCommands.Clear();

        if (string.IsNullOrEmpty(query))
        {
            foreach (var c in _allCommands) FilteredCommands.Add(c);
            return;
        }

        // Fuzzy-rank commands and recent paths together.
        var scored = new List<(int score, CommandItem item)>();
        foreach (var c in _allCommands)
        {
            var s = c.FuzzyScore(query);
            if (s >= 0) scored.Add((s, c));
        }
        foreach (var path in _recentPaths)
        {
            var s = CommandItem.FuzzyScore(path, query);
            if (s >= 0)
            {
                var captured = path;
                scored.Add((s, new CommandItem(path, "Recent", vm => vm.NavigateActive(captured))));
            }
        }

        foreach (var entry in scored.OrderByDescending(t => t.score).Take(MaxPaletteResults))
        {
            FilteredCommands.Add(entry.item);
        }
    }

    // ---- tab commands ----

    [RelayCommand]
    private void OpenNewTab()
    {
        var start = ActiveTab?.ActivePane.CurrentPath ?? string.Empty;
        if (string.IsNullOrEmpty(start) || start.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        var tab = TabViewModel.Create(start);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab()
    {
        if (ActiveTab is null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        var closing = ActiveTab;
        Tabs.Remove(closing);
        closing.Dispose();
        if (Tabs.Count == 0)
        {
            OpenNewTab();
            return;
        }
        ActiveTab = idx >= 0 && idx < Tabs.Count ? Tabs[idx] : Tabs[^1];
    }

    private bool CanCloseTab() => Tabs.Count > 1;

    /// <summary>Cycles the active tab (mouse-wheel over the tab strip).</summary>
    public void CycleTab(int delta)
    {
        if (Tabs.Count == 0 || ActiveTab is null) return;
        var i = Tabs.IndexOf(ActiveTab);
        i = ((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[i];
    }

    /// <summary>Navigates the active pane, recording the destination in recent paths.</summary>
    public void NavigateActive(string path)
    {
        ActiveTab?.ActivePane.NavigateTo(path);
        RecordRecent(path);
    }

    private void RecordRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _recentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentPaths.Insert(0, path);
        if (_recentPaths.Count > MaxRecentPaths) _recentPaths.RemoveRange(MaxRecentPaths, _recentPaths.Count - MaxRecentPaths);
    }

    [RelayCommand]
    private void ToggleTheme() => ServiceLocator.Theme.ToggleTheme();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void ToggleDualPane()
    {
        if (ActiveTab != null) ActiveTab.IsDualPane = !ActiveTab.IsDualPane;
    }

    [RelayCommand]
    private void CopyActivePath()
    {
        if (ActiveTab is null) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
            {
                desk.MainWindow?.Clipboard?.SetTextAsync(ActiveTab.ActivePane.CurrentPath);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CopyActivePath: {ex.Message}"); }
    }

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            // Fold currently open locations into recent paths, then reset the query.
            foreach (var t in Tabs) RecordRecent(t.ActivePane.CurrentPath);
            CommandPaletteQuery = string.Empty;
            RefreshPalette(string.Empty);
        }
    }

    [RelayCommand]
    private void ExecuteCommand(CommandItem command)
    {
        if (command?.Execute is null) return;
        command.Execute(this);
        IsCommandPaletteOpen = false;
    }

    partial void OnActiveTabChanged(TabViewModel? value) => CloseTabCommand.NotifyCanExecuteChanged();

    public event EventHandler? SettingsRequested;

    /// <summary>Forwards an OS light/dark change to the theme service.</summary>
    public void NotifySystemThemeChanged(bool isDark) => ServiceLocator.Theme.NotifySystemThemeChanged(isDark);

    // ---- session persistence ----

    private void RestoreSession()
    {
        try
        {
            var session = ServiceLocator.Session.Load();
            IsSidebarOpen = session.SidebarOpen;
            _recentPaths.AddRange(session.RecentPaths.Take(MaxRecentPaths));

            foreach (var ts in session.Tabs)
            {
                Tabs.Add(TabViewModel.Restore(ts));
            }
            if (Tabs.Count > 0)
            {
                var idx = Math.Clamp(session.ActiveTabIndex, 0, Tabs.Count - 1);
                ActiveTab = Tabs[idx];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RestoreSession failed: {ex.Message}");
        }
    }

    public void SaveSession()
    {
        try
        {
            var state = new SessionState
            {
                SidebarOpen = IsSidebarOpen,
                ActiveTabIndex = ActiveTab != null ? Math.Max(0, Tabs.IndexOf(ActiveTab)) : 0,
                RecentPaths = _recentPaths.Take(MaxRecentPaths).ToList(),
                Tabs = Tabs.Select(t => t.CaptureState()).ToList()
            };
            ServiceLocator.Session.Save(state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveSession failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        SaveSession();
        foreach (var t in Tabs) t.Dispose();
        Tabs.Clear();
        ServiceLocator.Theme.ThemeChanged -= OnThemeChanged;
    }
}
