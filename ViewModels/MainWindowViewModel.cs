using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>Root view-model — owns tabs and the command palette overlay state.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
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
    private bool _commandsBuilt;

    public MainWindowViewModel()
    {
        // Hook theme changes so views bound to accent/folder colours refresh.
        ServiceLocator.Theme.ThemeChanged += OnThemeChanged;

        OpenNewTabCommand.Execute(null);

        foreach (var root in SidebarNode.BuildRoots())
        {
            Sidebar.Add(root);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Trigger a property notification for any bound "AccentBrush" / folder colour,
        // by raising our own OnPropertyChanged surrogate. Views typically bind to the
        // theme service directly so this is purely a notification convenience.
        OnPropertyChanged(nameof(ThemeAccent));
    }

    /// <summary>The active accent colour for headers/buttons.</summary>
    public Avalonia.Media.Color ThemeAccent => ServiceLocator.Theme.Accent;
    public Avalonia.Media.SolidColorBrush ThemeAccentBrush => new(ServiceLocator.Theme.Accent);

    /// <summary>Build (once) and expose the full command catalog.</summary>
    private void EnsureCommandsBuilt()
    {
        if (_commandsBuilt) return;
        _commandsBuilt = true;

        _allCommands.Add(new CommandItem("New Tab", "File", vm => vm.OpenNewTab(), "Ctrl+T"));
        _allCommands.Add(new CommandItem("Close Tab", "File", vm => vm.CloseTab(), "Ctrl+W"));
        _allCommands.Add(new CommandItem("Toggle Theme", "Appearance", vm => ServiceLocator.Theme.ToggleTheme(), "Ctrl+Shift+T"));
        _allCommands.Add(new CommandItem("Open Settings", "Application", vm => vm.OpenSettings()));
        _allCommands.Add(new CommandItem("Toggle Sidebar", "View", vm => vm.IsSidebarOpen = !vm.IsSidebarOpen, "Ctrl+B"));
        _allCommands.Add(new CommandItem("Toggle Dual Pane", "View", vm =>
        {
            if (vm.ActiveTab != null) vm.ActiveTab.IsDualPane = !vm.ActiveTab.IsDualPane;
        }, "Ctrl+D"));
        _allCommands.Add(new CommandItem("Swap Panes", "View", vm => vm.ActiveTab?.SwapPanes()));
        _allCommands.Add(new CommandItem("Git: Commit", "Git", _ =>
        {
            System.Diagnostics.Debug.WriteLine("Command palette: Git: Commit (stub)");
        }));
        _allCommands.Add(new CommandItem("Theme: Toggle Dark", "Appearance", vm => ServiceLocator.Theme.ToggleTheme()));

        FilteredCommands.Clear();
        foreach (var c in _allCommands) FilteredCommands.Add(c);
    }

    partial void OnCommandPaletteQueryChanged(string value)
    {
        EnsureCommandsBuilt();
        FilteredCommands.Clear();
        if (string.IsNullOrEmpty(value))
        {
            foreach (var c in _allCommands) FilteredCommands.Add(c);
            return;
        }

        // Linear in-memory scan avoiding LINQ allocations on the hot path.
        foreach (var c in _allCommands)
        {
            if (c.SearchText.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCommands.Add(c);
            }
        }
    }

    [RelayCommand]
    private void OpenNewTab()
    {
        var tab = TabViewModel.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab()
    {
        if (ActiveTab is null) return;
        int idx = Tabs.IndexOf(ActiveTab);
        Tabs.Remove(ActiveTab);
        ActiveTab.Dispose();
        if (Tabs.Count == 0)
        {
            OpenNewTab();
            return;
        }
        ActiveTab = idx >= 0 && idx < Tabs.Count ? Tabs[idx] : Tabs[^0];
    }

    private bool CanCloseTab() => Tabs.Count > 1;

    [RelayCommand]
    private void ToggleTheme() => ServiceLocator.Theme.ToggleTheme();

    [RelayCommand]
    private void OpenSettings()
    {
        System.Diagnostics.Debug.WriteLine("OpenSettings: stub (would raise SettingsRequested)");
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

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
        string path = ActiveTab.ActivePane.CurrentPath;
        try
        {
            System.Diagnostics.Debug.WriteLine($"CopyActivePath → {path}");
        }
        catch (Exception) { /* best effort */ }
    }

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            CommandPaletteQuery = string.Empty;
            EnsureCommandsBuilt();
        }
    }

    [RelayCommand]
    private void ExecuteCommand(CommandItem command)
    {
        if (command?.Execute is null) return;
        command.Execute(this);
        IsCommandPaletteOpen = false;
    }

    partial void OnActiveTabChanged(TabViewModel? value)
    {
        CloseTabCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler? SettingsRequested;

    public void Dispose()
    {
        foreach (var t in Tabs) t.Dispose();
        Tabs.Clear();
        ServiceLocator.Theme.ThemeChanged -= OnThemeChanged;
    }
}